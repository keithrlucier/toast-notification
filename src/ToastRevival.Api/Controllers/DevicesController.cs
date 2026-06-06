using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Extensions;
using ToastRevival.Api.Hubs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuditService _audit;
    private readonly ILicenseService _license;
    private readonly IStripeBillingSyncService _billingSync;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        AppDbContext db,
        ITokenService tokens,
        IAuditService audit,
        ILicenseService license,
        IStripeBillingSyncService billingSync,
        IHubContext<NotificationHub> hubContext,
        IConfiguration config,
        ILogger<DevicesController> logger)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
        _license = license;
        _billingSync = billingSync;
        _hubContext = hubContext;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Called by the agent on first run. No authentication required.
    /// TenantId comes from the MSI property set by the MSP during deployment.
    ///
    /// Enrollment key gating: when a tenant has an EnrollmentKey set, the
    /// request must include the matching key or registration is rejected with
    /// 403. Tenants without an EnrollmentKey allow open registration.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<ActionResult<DeviceTokenResponse>> Register([FromBody] RegisterDeviceRequest req)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == req.TenantId);
        // DOS-M4: Return a generic 400 instead of 404 for missing tenant to avoid
        // leaking whether a given TenantId GUID is valid before enrollment key is checked.
        if (tenant is null) return BadRequest("Registration failed.");

        // XT-1 — device enrollment gate. A tenant may have single-use, expiring,
        // dashboard-issued EnrollmentTokens and/or the legacy reusable per-tenant
        // EnrollmentKey. The agent presents whichever value the MSI wrote to the HKLM
        // bootstrap in the same req.EnrollmentKey field, so this needs no agent or
        // installer change. We try the single-use token first (consuming it atomically
        // and binding it to this device identity), then fall back to the legacy key.
        // A spent token left behind in a device's registry cannot provision a NEW rogue
        // device — that is the XT-1 win. When the tenant has neither mechanism,
        // registration stays open (unchanged).
        // INJ-L1: Trim user-supplied device identity strings before use.
        var deviceName = req.DeviceName.Trim();
        var username   = req.Username.Trim();

        var tenantHasLegacyKey = !string.IsNullOrWhiteSpace(tenant.EnrollmentKey);
        var tenantHasTokens = await _db.EnrollmentTokens.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == req.TenantId);
        if (tenantHasLegacyKey || tenantHasTokens)
        {
            if (!await PassesEnrollmentGateAsync(req.TenantId, tenant.EnrollmentKey,
                                                 req.EnrollmentKey, deviceName, username))
            {
                return StatusCode(403, "Invalid or expired enrollment token.");
            }
        }

        // Idempotent registration. The MSI uninstall wipes per-user config.json on
        // purpose (so a reinstall starts clean), but that meant every reinstall on
        // the same machine produced a brand-new Device row, leaving the old row
        // orphaned — visible to admins as duplicates and (worse) double-counting
        // against the seat license. Match on TenantId + DeviceName + Username and
        // refresh credentials on the existing (non-decommissioned) row instead of
        // creating a sibling. Decommissioned rows are not reused — those represent
        // a deliberate admin action and a re-registration should provision a new
        // device row + go through the license CanRegisterDeviceAsync gate.

        var existing = await _db.Devices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d =>
                d.TenantId == req.TenantId &&
                d.DeviceName == deviceName &&
                d.Username == username &&
                d.Status != DeviceStatus.Decommissioned);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = HashToken(rawToken);

        Device device;
        string auditAction;
        if (existing is not null)
        {
            // XT-2: the re-register branch reuses an existing seat, so the license
            // *cap* legitimately doesn't apply (no new seat consumed). But it
            // bypassed TryRegisterDeviceAtomicAsync entirely, which also skipped
            // the suspension gate (LicenseService.IsWithinCap → SuspendedAt). A
            // suspended tenant must not get fresh device credentials, so enforce
            // the suspension check here before reactivating the row.
            if (tenant.SuspendedAt.HasValue)
                return StatusCode(403, "Tenant suspended. Device registration is disabled.");

            existing.OsVersion = req.OsVersion;
            existing.AgentVersion = req.AgentVersion;
            existing.RegistrationToken = tokenHash;
            existing.Status = DeviceStatus.Active;
            // M1 — WAN is server-derived, always refresh it. LAN is agent-reported:
            // only overwrite when the incoming value is non-empty so an old agent
            // (no LAN in payload) never nulls out a previously captured value.
            existing.WanIpAddress = ClampIp(CloudflareIpValidator.ResolveTrustedClientIp(HttpContext));
            if (!string.IsNullOrWhiteSpace(req.LanIpAddress))
                existing.LanIpAddress = ClampIp(req.LanIpAddress);
            device = existing;
            auditAction = "device.re-register";
            await _db.SaveChangesAsync();
            // Seat count is unchanged — we're reusing an existing row, not adding one.
        }
        else
        {
            // License enforcement applies only to NEW seats. A reinstall on an
            // existing seat (handled above) must not be blocked by license
            // depletion. The cap check + device INSERT + ConsumedCount bump
            // run atomically inside the service under a per-tenant advisory
            // lock so two concurrent registrations for the same trial tenant
            // can't both pass the 2-device gate before either commits.
            device = new Device
            {
                TenantId = req.TenantId,
                DeviceName = deviceName,
                Username = username,
                OsVersion = req.OsVersion,
                AgentVersion = req.AgentVersion,
                RegistrationToken = tokenHash,
                // M1 — WAN server-derived; LAN straight from the (new) agent payload.
                WanIpAddress = ClampIp(CloudflareIpValidator.ResolveTrustedClientIp(HttpContext)),
                LanIpAddress = ClampIp(req.LanIpAddress),
            };

            if (!await _license.TryRegisterDeviceAtomicAsync(tenant, device))
            {
                return StatusCode(403, "Subscription canceled. Please renew to register devices.");
            }

            auditAction = "device.register";

            // Stripe sync stays outside the transaction — it's network I/O and
            // safe to retry; the seat is already committed.
            await _billingSync.SyncSubscriptionQuantityAsync(tenant);
        }

        var jwt = _tokens.CreateDeviceToken(device);

        // M1 — bare RemoteIpAddress returns the Cloudflare edge IP in prod; use the
        // trusted-client resolver (CF-Connecting-IP / XFF aware) like the rate limiter.
        await _audit.LogAsync(req.TenantId, null, auditAction, "Device",
            device.Id.ToString(), new { device.DeviceName, device.Username },
            CloudflareIpValidator.ResolveTrustedClientIp(HttpContext));

        return Ok(new DeviceTokenResponse(jwt, device.Id, req.TenantId, tenant.SigningKey, tenant.Name));
    }

    [Authorize]
    [HttpGet]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<ActionResult<IEnumerable<DeviceResponse>>> List()
    {
        // MT-H1: Explicit TenantId predicate as defense-in-depth alongside EF global filter.
        // REVIEW-2026-06-06 REST-M6 REJECTED-by-design: unbounded device list is known design debt; pagination requires coordinated API+frontend change to avoid breaking Compose page multi-select; filed as PERF-backlog
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var devices = await _db.Devices
            .Include(d => d.GroupMemberships)
            .ThenInclude(m => m.DeviceGroup)
            .Where(d => d.TenantId == tenantId && d.Status != DeviceStatus.Decommissioned)
            .OrderBy(d => d.DeviceName)
            .ToListAsync();

        return Ok(devices.Select(ToResponse));
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceResponse>> Get(Guid id)
    {
        // MT-H1: Explicit TenantId predicate as defense-in-depth alongside EF global filter.
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var device = await _db.Devices
            .Include(d => d.GroupMemberships)
            .ThenInclude(m => m.DeviceGroup)
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId && d.Status != DeviceStatus.Decommissioned);

        return device is null ? NotFound() : Ok(ToResponse(device));
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Decommission(Guid id)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var device = await _db.Devices.FindAsync(id);
        if (device is null) return NotFound();
        if (device.TenantId != tenantId) return NotFound();

        device.Status = DeviceStatus.Decommissioned;
        await _db.SaveChangesAsync();

        // M6 D4: maintain ConsumedCount — use device.TenantId (same as tenantId after guard above)
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == device.TenantId);
        if (tenant is not null)
        {
            await _license.DecrementConsumedAsync(tenant);
            await _billingSync.SyncSubscriptionQuantityAsync(tenant);
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "device.decommission", "Device", id.ToString());

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // XT-1 — admin enrollment-token management (per-device, single-use, expiring).
    // Admin-only, tenant-scoped. The issued token is pasted into the MSI deploy
    // command's ENROLLMENTKEY=... slot in place of the reusable per-tenant key.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issue a single-use enrollment token. The plaintext is returned exactly once;
    /// only its SHA-256 hash is persisted. One token per device.
    /// </summary>
    [Authorize]
    [HttpPost("enrollment-tokens")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<ActionResult<IssuedEnrollmentTokenResponse>> IssueEnrollmentToken(
        [FromBody] IssueEnrollmentTokenRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (Guid?)null;

        var ttlHours = Math.Clamp(req.TtlHours ?? 24, 1, 168);
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();

        var token = new EnrollmentToken
        {
            TenantId = tenantId,
            TokenHash = HashToken(rawToken),
            Label = label,
            CreatedByUserId = userId,
            ExpiresAt = DateTime.UtcNow.AddHours(ttlHours),
        };
        _db.EnrollmentTokens.Add(token);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, userId, "enrollment-token.issue", "EnrollmentToken",
            token.Id.ToString(), new { token.Label, token.ExpiresAt },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new IssuedEnrollmentTokenResponse(token.Id, rawToken, token.ExpiresAt, token.Label));
    }

    /// <summary>List this tenant's enrollment tokens (newest first). Never returns plaintext.</summary>
    [Authorize]
    [HttpGet("enrollment-tokens")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<ActionResult<IEnumerable<EnrollmentTokenDto>>> ListEnrollmentTokens()
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var now = DateTime.UtcNow;

        var tokens = await _db.EnrollmentTokens
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(tokens.Select(t => new EnrollmentTokenDto(
            t.Id, t.Label, EnrollmentTokenStatus(t, now), t.CreatedAt, t.ExpiresAt,
            t.UsedAt, t.UsedByDeviceName, t.UsedByUsername, t.RevokedAt)));
    }

    /// <summary>
    /// Revoke an enrollment token. An unredeemed token can no longer be used; a token
    /// already used to register a device is unaffected (the device keeps its JWT).
    /// </summary>
    [Authorize]
    [HttpDelete("enrollment-tokens/{id:guid}")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<IActionResult> RevokeEnrollmentToken(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var token = await _db.EnrollmentTokens.FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);
        if (token is null) return NotFound();

        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(tenantId, token.RevokedByUserId, "enrollment-token.revoke", "EnrollmentToken",
                token.Id.ToString(), null, HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        return NoContent();
    }

    /// <summary>
    /// Returns the current tenant display name for notification attribution.
    /// Called by the agent on every startup so toasts reflect the latest name
    /// even if the admin renamed the tenant after the device registered.
    /// Device-JWT only (requires "deviceId" claim).
    /// </summary>
    [Authorize]
    [HttpGet("tenant-name")]
    public async Task<ActionResult<TenantAttributionResponse>> GetTenantName()
    {
        var deviceIdClaim = User.FindFirstValue("deviceId");
        if (!Guid.TryParse(deviceIdClaim, out var deviceId)) return Unauthorized();

        var tenantIdClaim = User.FindFirstValue("tenantId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId)) return Unauthorized();

        // SES-3 + SES-2: reject decommissioned-device OR suspended-tenant tokens.
        if (await IsDeviceRevoked(deviceId)) return Unauthorized();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        return Ok(new TenantAttributionResponse(tenant.Name, ToPublicUrl(tenant.LogoUrl)));
    }

    /// <summary>
    /// M12 — bundled device-appearance config (desktop overlay + lock screen) in
    /// one round-trip. Called by the agent at startup and on reconnect, right
    /// after the tenant-name refresh. Device-JWT only (requires "deviceId" claim).
    /// A non-200 here is non-fatal — the agent keeps whatever it last applied.
    /// </summary>
    [Authorize]
    [HttpGet("appearance-config")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<ActionResult<AppearanceConfigResponse>> GetAppearanceConfig()
    {
        var deviceIdClaim = User.FindFirstValue("deviceId");
        if (!Guid.TryParse(deviceIdClaim, out var deviceId)) return Unauthorized();

        var tenantIdClaim = User.FindFirstValue("tenantId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId)) return Unauthorized();

        // SES-3 + SES-2: reject decommissioned-device OR suspended-tenant tokens.
        if (await IsDeviceRevoked(deviceId)) return Unauthorized();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        // Lock screen URL is stored as a /assets/lockscreen/ relative path
        // (see TenantController.NormalizeLockScreenUrlForStorage); the agent's
        // LockScreenService strictly requires an http(s) URL, so absolutize here.
        var overlay    = TenantAppearance.BuildOverlay(tenant);
        var lockScreen = TenantAppearance.BuildLockScreen(tenant) with
        {
            ImageUrl = ToPublicUrl(tenant.LockScreenImageUrl)
        };
        // AGT-4-R: HMAC-sign the exact JSON the agent will verify + apply.
        // The response carries BOTH the unsigned overlay/lockScreen (so a pre-0.4.35 agent
        // keeps working — it ignores the extra fields) AND the signed payload + signature.
        // A 0.4.35+ agent verifies the signature and applies ONLY the signed payload, never
        // the unsigned top-level fields.
        var (signedPayload, signature) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, tenant.SigningKey);
        return Ok(new AppearanceConfigResponse(overlay, lockScreen, signedPayload, signature));
    }

    // Called by agent to confirm it's still alive (heartbeat).
    // Optional body: { "agentVersion": "0.4.26" } — written to Device.AgentVersion so
    // the dashboard reflects the installed version after an MSI upgrade (which reuses
    // the existing config.json and never re-registers).
    [Authorize]
    [HttpPost("ping")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<IActionResult> Ping([FromBody] PingRequest? body = null)
    {
        var deviceIdClaim = User.FindFirstValue("deviceId");
        if (!Guid.TryParse(deviceIdClaim, out var deviceId)) return Unauthorized();

        var device = await _db.Devices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return NotFound();

        // SES-3: a decommissioned device's 365-day JWT stays cryptographically
        // valid; reject heartbeats from it (mirrors the hub).
        if (device.Status == DeviceStatus.Decommissioned) return Unauthorized();
        // PERF-M5: use a projected scalar instead of loading the full Tenant row.
        // FIX-SES-2: same kill-switch for a suspended tenant's devices.
        var tenantSuspendedAt = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == device.TenantId)
            .Select(t => t.SuspendedAt)
            .FirstOrDefaultAsync();
        if (tenantSuspendedAt != null) return Unauthorized();

        device.LastPing = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(body?.AgentVersion))
            device.AgentVersion = body.AgentVersion;
        // M1 — refresh network context every heartbeat so the dashboard tracks
        // network changes (VPN, DHCP, Wi-Fi roaming) within the ~6-min ping cadence.
        // WAN is server-derived (always refresh). LAN only when the agent sends one,
        // so an old agent's empty payload never nulls a stored value.
        device.WanIpAddress = ClampIp(CloudflareIpValidator.ResolveTrustedClientIp(HttpContext));
        if (!string.IsNullOrWhiteSpace(body?.LanIpAddress))
            device.LanIpAddress = ClampIp(body.LanIpAddress);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Returns the current latest agent version and MSI download URL. Anonymous —
    /// the agent polls this without a token so it works before the device is online.
    /// Values are configured via Agent:LatestVersion + Agent:MsiDownloadUrl in
    /// appsettings (env-var overridden in production).
    /// </summary>
    // REVIEW-2026-06-06 REST-L4 REJECTED-by-design: ETag conditional request handling requires agent-side If-None-Match support; bandwidth saving is minimal at current fleet scale; documented as PERF backlog
    [HttpGet("/api/agent/version")]
    [AllowAnonymous]
    public IActionResult GetAgentVersion()
    {
        var version     = _config["Agent:LatestVersion"];
        var downloadUrl = _config["Agent:MsiDownloadUrl"];

        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(downloadUrl))
            return NotFound("Agent version info not configured.");

        return Ok(new { version, msiDownloadUrl = downloadUrl });
    }

    /// <summary>
    /// Metadata for the canonical clean-removal script served statically at
    /// /downloads/ (alongside the MSI). The admin "Remove agent" modal shows the
    /// download link plus the script's real last-modified date. Anonymous and
    /// read-only — the script contains no secrets (it removes by name, not key).
    /// </summary>
    [HttpGet("/api/agent/uninstall-script-info")]
    [AllowAnonymous]
    public IActionResult GetUninstallScriptInfo()
    {
        // Relative URL by default so the download is same-origin (works with the
        // browser's download attribute on any deployment host).
        var url  = _config["Agent:UninstallScriptUrl"] ?? "/downloads/uninstall-toast-agent.ps1";
        var root = _config["Downloads:RootPath"]       ?? "/opt/toast/downloads";
        var path = System.IO.Path.Combine(root, "uninstall-toast-agent.ps1");

        DateTime? lastModifiedUtc = null;
        long sizeBytes = 0;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var fi = new System.IO.FileInfo(path);
                lastModifiedUtc = fi.LastWriteTimeUtc;
                sizeBytes       = fi.Length;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "uninstall-script-info: could not stat {Path}", path);
        }

        return Ok(new { url, lastModifiedUtc, sizeBytes });
    }

    /// <summary>
    /// Metadata for the pre-wrapped Intune Win32 package (.intunewin) served
    /// statically at /downloads/ (alongside the MSI). The admin Install Agent page
    /// shows a Download button plus version/size/last-modified, and hides the
    /// button when the file is not present (Downloads:RootPath not yet populated).
    /// Anonymous and read-only — the package is identical for every tenant and
    /// carries no secrets; the per-tenant values live in the install command the
    /// dashboard builds from the authenticated tenant's settings.
    /// </summary>
    [HttpGet("/api/agent/intunewin-info")]
    [AllowAnonymous]
    public IActionResult GetIntuneWinInfo()
    {
        const string fileName = "ToastNotification.intunewin";
        // Relative URL by default so the download is same-origin (works with the
        // browser's download attribute on any deployment host).
        var url     = _config["Agent:IntuneWinUrl"] ?? "/downloads/" + fileName;
        var version = _config["Agent:LatestVersion"];
        var root    = _config["Downloads:RootPath"] ?? "/opt/toast/downloads";
        var path    = System.IO.Path.Combine(root, fileName);

        DateTime? lastModifiedUtc = null;
        long sizeBytes = 0;
        bool available = false;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var fi = new System.IO.FileInfo(path);
                lastModifiedUtc = fi.LastWriteTimeUtc;
                sizeBytes       = fi.Length;
                available       = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "intunewin-info: could not stat {Path}", path);
        }

        return Ok(new { url, version, available, lastModifiedUtc, sizeBytes });
    }

    /// <summary>
    /// Decommissions a device AND pushes "UninstallAgent" to it via the SignalR hub
    /// if it is currently connected. The agent restores the lock screen, writes the
    /// uninstall trigger file, and fires the SYSTEM updater task which runs
    /// msiexec /x. Requires admin role (more destructive than plain decommission).
    ///
    /// If the device is offline the decommission still happens; the agent software
    /// will not be removed but the device won't be able to reconnect.
    /// </summary>
    [Authorize]
    [HttpPost("{id:guid}/uninstall")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<IActionResult> RequestUninstall(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var device = await _db.Devices.FindAsync(id);
        if (device is null) return NotFound();
        if (device.TenantId != tenantId) return NotFound();

        device.Status = DeviceStatus.Decommissioned;
        await _db.SaveChangesAsync();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == device.TenantId);
        if (tenant is not null)
        {
            await _license.DecrementConsumedAsync(tenant);
            await _billingSync.SyncSubscriptionQuantityAsync(tenant);
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "device.uninstall", "Device", id.ToString());

        // Push to the device if it's currently connected on the hub.
        if (NotificationHub.ConnectedDevices.TryGetValue(id, out var connectionId))
        {
            try
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("UninstallAgent");
            }
            catch (Exception ex)
            {
                // Non-fatal — device is decommissioned regardless. Agent will
                // handle DeviceDecommissioned on next reconnect.
                _logger?.LogWarning(ex, "UninstallAgent hub push failed for device {DeviceId}", id);
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Admin-triggered "check for update now" for a single device. If the device is
    /// online it gets the CheckForUpdate hub command and runs its self-update check
    /// immediately instead of waiting for the 24h poll; if offline it picks the new
    /// version up on its next scheduled poll. The agent applies its own guards
    /// (Velopack-managed / DisableAutoUpdate) on receipt, so this never forces an
    /// update against local policy. Returns whether the device was reached.
    /// </summary>
    [Authorize]
    [HttpPost("{id:guid}/check-update")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<IActionResult> RequestUpdateCheck(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var device = await _db.Devices.FindAsync(id);
        if (device is null) return NotFound();
        if (device.TenantId != tenantId) return NotFound();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "device.check-update", "Device", id.ToString());

        var pushed = false;
        if (NotificationHub.ConnectedDevices.TryGetValue(id, out var connectionId))
        {
            try
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("CheckForUpdate");
                pushed = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CheckForUpdate hub push failed for device {DeviceId}", id);
            }
        }

        return Ok(new { pushed });
    }

    /// <summary>
    /// Admin-triggered fleet update push: sends CheckForUpdate to every ONLINE
    /// device in the caller's tenant so the whole fleet rolls forward at once
    /// instead of waiting on individual 24h timers. Offline devices are skipped
    /// and update on their next poll. Returns how many online devices were reached
    /// and the tenant's total active device count.
    /// </summary>
    [Authorize]
    [HttpPost("check-update-all")]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<IActionResult> RequestUpdateCheckAll()
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        var deviceIds = await _db.Devices
            .Where(d => d.TenantId == tenantId && d.Status != DeviceStatus.Decommissioned)
            .Select(d => d.Id)
            .ToListAsync();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "device.check-update-all", "Tenant", tenantId.ToString());

        // Resolve to live hub connections for THIS tenant only, then one batched push.
        var connIds = new List<string>();
        foreach (var did in deviceIds)
            if (NotificationHub.ConnectedDevices.TryGetValue(did, out var cid))
                connIds.Add(cid);

        if (connIds.Count > 0)
        {
            try
            {
                await _hubContext.Clients.Clients(connIds).SendAsync("CheckForUpdate");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CheckForUpdate fleet push failed for tenant {TenantId}", tenantId);
            }
        }

        return Ok(new { pushed = connIds.Count, total = deviceIds.Count });
    }

    /// <summary>
    /// ARCH-M2: Delegates to the shared DbContextExtensions.IsDeviceRevokedAsync.
    /// </summary>
    private Task<bool> IsDeviceRevoked(Guid deviceId) =>
        _db.IsDeviceRevokedAsync(deviceId);

    /// <summary>
    /// ARCH-M1: Delegates to the shared ClaimsPrincipalExtensions.IsAdmin().
    /// </summary>
    private bool IsAdmin() => User.IsAdmin();

    private static DeviceResponse ToResponse(Device d) =>
        new(d.Id, d.DeviceName, d.Username, d.OsVersion, d.AgentVersion,
            d.Status.ToString(), d.LastPing, d.RegisteredAt,
            d.GroupMemberships
                .Where(m => m.DeviceGroup.TenantId == d.TenantId)
                .Select(m => m.DeviceGroupId)
                .Distinct()
                .ToList(),
            d.WanIpAddress, d.LanIpAddress);

    private string? ToPublicUrl(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        // Require http(s) on the "already absolute" branch. On Linux,
        // Uri.TryCreate("/foo", Absolute, ...) returns true with Scheme="file",
        // which would return "/assets/lockscreen/..." untouched and break
        // LockScreenService's strict-absolute check on the agent.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var existing)
            && existing.Scheme is "http" or "https") return trimmed;
        if (!trimmed.StartsWith('/')) return trimmed;

        // INJ-M2: Use Request.Scheme and Request.Host (ForwardedHeaders-validated) instead
        // of reading raw X-Forwarded-Proto/X-Forwarded-Host headers directly.
        var scheme = Request.Scheme;
        var host = Request.Host.Value;

        return $"{scheme}://{host}{trimmed}";
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }

    // M1 — defensive clamp for the Wan/LanIpAddress varchar(64) columns. A real
    // IPv4/IPv6 string is well under 64 chars; this only guards against an
    // authenticated agent sending an over-length value, which would otherwise
    // raise an Npgsql 22001 truncation error and 500 the register/ping write.
    // Null/empty passes through unchanged so callers keep their own non-empty guards.
    private const int IpColumnMaxLength = 64;
    private static string? ClampIp(string? value) =>
        value is { Length: > IpColumnMaxLength } ? value[..IpColumnMaxLength] : value;

    /// <summary>
    /// XT-1 enrollment gate. Returns true when <paramref name="presented"/> is an
    /// acceptable single-use token (consumed atomically here and bound to the device
    /// identity) or matches the legacy per-tenant key. Called from the anonymous
    /// Register endpoint, so every read IgnoreQueryFilters and scopes by tenantId.
    /// </summary>
    private async Task<bool> PassesEnrollmentGateAsync(
        Guid tenantId, string? legacyKey, string? presented, string deviceName, string username)
    {
        if (string.IsNullOrWhiteSpace(presented)) return false;

        // 1) Single-use token path (preferred). Look up by hash of the presented value.
        var hash = HashToken(presented);
        var token = await _db.EnrollmentTokens.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TokenHash == hash);
        if (token is not null)
        {
            if (token.RevokedAt is not null) return false;

            if (token.UsedAt is null)
            {
                if (token.ExpiresAt < DateTime.UtcNow) return false;

                // Atomic single-use claim: the conditional UPDATE flips UsedAt only if
                // it is still null, so two devices racing the same token cannot both
                // pass — the check and the state-change are one SQL statement (the
                // "exactly one fire" rule). ExecuteUpdate bypasses the tracker, hence
                // the AsNoTracking reads around it.
                var now = DateTime.UtcNow;
                var claimed = await _db.EnrollmentTokens.IgnoreQueryFilters()
                    // XT-L1 — tenant scope in the atomic claim (defense in depth; the
                    // lookup above is already tenant-scoped, but every query touching
                    // tenant data carries the predicate, not inferring it).
                    .Where(t => t.Id == token.Id && t.TenantId == tenantId && t.UsedAt == null && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.UsedAt, now)
                        .SetProperty(t => t.UsedByDeviceName, deviceName)
                        .SetProperty(t => t.UsedByUsername, username));
                if (claimed == 1) return true;

                // Lost the race — re-read fresh and let the same-device reinstall rule decide.
                // XT-L2 — keep the tenant predicate on the re-read so a leaked token.Id
                // cannot surface another tenant's row into the reinstall carve-out below.
                token = await _db.EnrollmentTokens.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == token.Id && t.TenantId == tenantId);
                if (token is null || token.RevokedAt is not null) return false;
            }

            // Already used — allow only a reinstall of the SAME machine (the MSI wipes
            // config.json on uninstall, so the agent must re-register). The idempotent
            // lookup below reuses the existing row, so this never mints a new seat.
            //
            // XT-M1 (OPEN — owner: Keith; fix scheduled as XT-3) — "same machine" here is
            // the (DeviceName, Username) tuple the agent self-reports, which is also the
            // tuple the idempotent Device match (Register, above) keys on. These are NOT
            // hardware-backed: an attacker who reads a spent token out of HKLM AND knows
            // the original device name + username can re-enroll under that identity.
            // Severity is bounded (HKLM read already implies machine compromise), so this
            // is a defense-in-depth gap, not an open door.
            //
            // DECISION (2026-06-02, Keith, by phone): the "require a fresh token per
            // reinstall" option is REJECTED — it breaks silent RMM mass deployment across
            // hundreds of devices (every reinstall would need a freshly issued token).
            // The carve-out STAYS as-is. The proper fix is to bind the token to a hardware
            // identifier (the machine SID already computed agent-side for the lock-screen
            // path) — a cross-component change (agent payload + UsedByMachineSid column +
            // migration + backward-compatible carve-out during agent rollout). That work is
            // scoped as build-mode project XT-3 (see Docs/ToastRevival/projects/XT-3/).
            // This anchor keeps XT-M1 from being re-flagged as un-triaged until XT-3 ships.
            //
            // REVIEW-2026-06-06 AA-M7 REJECTED-by-design: self-reported (DeviceName, Username) identity in reinstall carve-out is bounded by requiring read access to HKLM token; machine SID binding planned as XT-3 per Keith's explicit product decision (2026-06-02 phone); see XT-M1 carve-out anchor in DevicesController
            var matches = string.Equals(token.UsedByDeviceName, deviceName, StringComparison.Ordinal)
                && string.Equals(token.UsedByUsername, username, StringComparison.Ordinal);

            // INJ-L3: Emit an audit warning whenever the reinstall carve-out fires,
            // so operators can detect unexpected reuse of spent enrollment tokens.
            if (matches)
            {
                await _audit.LogAsync(tenantId, null,
                    "DeviceReinstallCarveout", "EnrollmentToken", token.Id.ToString(),
                    new { deviceName, username, note = "Reinstall via spent token — self-reported identity. Pending XT-3 SID binding." },
                    ipAddress: null);
            }

            return matches;
        }

        // 2) Legacy reusable per-tenant key fallback (constant-time compare).
        if (!string.IsNullOrWhiteSpace(legacyKey)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(legacyKey)))
        {
            return true;
        }

        return false;
    }

    private static string EnrollmentTokenStatus(EnrollmentToken t, DateTime now) =>
        t.RevokedAt is not null ? "revoked"
        : t.UsedAt is not null ? "used"
        : t.ExpiresAt < now ? "expired"
        : "active";
}
