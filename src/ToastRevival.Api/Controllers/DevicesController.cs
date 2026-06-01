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
        if (tenant is null) return NotFound("Tenant not found.");

        // ANCHOR XT-1 (owner: Keith — ARCHITECTURAL + execution-context-sensitive).
        // The compare below is constant-time and sound, but the EnrollmentKey itself is
        // a single, reusable, non-expiring per-tenant secret that the MSI writes to
        // HKLM\SOFTWARE\Toast2IT\Toast Notification with the default (world-readable)
        // ACL — so any standard local user on one enrolled endpoint can read it and
        // register a rogue device (which then receives a 365-day token + the tenant
        // SigningKey). The robust fix is a redesign: per-device, single-use, expiring,
        // dashboard-issued enrollment tokens (or admin approval of new device rows). A
        // naive HKLM ACL lockdown is NOT safe here — the agent reads the key in USER
        // context (DeviceConfig.TryLoadBootstrapFromRegistry), so a SYSTEM+Admins-only
        // ACL would break enrollment; closing it needs an install-time copy to a
        // user-readable per-machine location or a SYSTEM-context re-register path.
        // Held for Keith's call on the enrollment-flow redesign. Anchored so the next
        // sweep does not re-flag the reusable-key shape as an oversight.
        if (!string.IsNullOrWhiteSpace(tenant.EnrollmentKey))
        {
            if (string.IsNullOrWhiteSpace(req.EnrollmentKey) ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(req.EnrollmentKey),
                    System.Text.Encoding.UTF8.GetBytes(tenant.EnrollmentKey)))
            {
                return StatusCode(403, "Invalid enrollment key.");
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
                d.DeviceName == req.DeviceName &&
                d.Username == req.Username &&
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
                DeviceName = req.DeviceName,
                Username = req.Username,
                OsVersion = req.OsVersion,
                AgentVersion = req.AgentVersion,
                RegistrationToken = tokenHash,
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

        await _audit.LogAsync(req.TenantId, null, auditAction, "Device",
            device.Id.ToString(), new { device.DeviceName, device.Username },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new DeviceTokenResponse(jwt, device.Id, req.TenantId, tenant.SigningKey, tenant.Name));
    }

    [Authorize]
    [HttpGet]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<ActionResult<IEnumerable<DeviceResponse>>> List()
    {
        var devices = await _db.Devices
            .Include(d => d.GroupMemberships)
            .ThenInclude(m => m.DeviceGroup)
            .Where(d => d.Status != DeviceStatus.Decommissioned)
            .OrderBy(d => d.DeviceName)
            .ToListAsync();

        return Ok(devices.Select(ToResponse));
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceResponse>> Get(Guid id)
    {
        var device = await _db.Devices
            .Include(d => d.GroupMemberships)
            .ThenInclude(m => m.DeviceGroup)
            .Where(d => d.Id == id && d.Status != DeviceStatus.Decommissioned)
            .FirstOrDefaultAsync();

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
        return Ok(new AppearanceConfigResponse(overlay, lockScreen));
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
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return NotFound();

        // SES-3: a decommissioned device's 365-day JWT stays cryptographically
        // valid; reject heartbeats from it (mirrors the hub).
        if (device.Status == DeviceStatus.Decommissioned) return Unauthorized();
        // FIX-SES-2: same kill-switch for a suspended tenant's devices. ?. guards an
        // orphaned tenant FK (can't happen with the FK constraint, but fail safe).
        if (device.Tenant?.SuspendedAt != null) return Unauthorized();

        device.LastPing = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(body?.AgentVersion))
            device.AgentVersion = body.AgentVersion;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Returns the current latest agent version and MSI download URL. Anonymous —
    /// the agent polls this without a token so it works before the device is online.
    /// Values are configured via Agent:LatestVersion + Agent:MsiDownloadUrl in
    /// appsettings (env-var overridden in production).
    /// </summary>
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
    /// SES-3 + FIX-SES-2 (2026-06-01): true when a device JWT must be refused — the
    /// device row is missing or Decommissioned (SES-3), OR the owning tenant is
    /// suspended (SES-2). Suspension is the operator kill switch for a compromised/
    /// abusive tenant; without the tenant half a suspended tenant's agents keep
    /// pulling appearance config + branding and draining toasts on their 365-day
    /// tokens. Active/Inactive devices under an ACTIVE tenant pass. Mirrors the hub.
    /// (Instant revocation of live USER/operator sessions on suspend needs a token
    /// epoch / SecurityStamp pipeline — see REVIEW_LEDGER SES-2 remainder, owner: Keith.)
    /// </summary>
    private async Task<bool> IsDeviceRevoked(Guid deviceId)
    {
        var row = await _db.Devices.IgnoreQueryFilters()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Status, TenantSuspended = d.Tenant.SuspendedAt != null })
            .FirstOrDefaultAsync();
        return row is null
            || row.Status == DeviceStatus.Decommissioned
            || row.TenantSuspended;
    }

    private bool IsAdmin()
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? "";
        return role is "Admin" or "SuperAdmin" || User.HasClaim("platformAdmin", "true");
    }

    private static DeviceResponse ToResponse(Device d) =>
        new(d.Id, d.DeviceName, d.Username, d.OsVersion, d.AgentVersion,
            d.Status.ToString(), d.LastPing, d.RegisteredAt,
            d.GroupMemberships
                .Where(m => m.DeviceGroup.TenantId == d.TenantId)
                .Select(m => m.DeviceGroupId)
                .Distinct()
                .ToList());

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

        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(scheme)) scheme = Request.Scheme;

        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault()?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(host)) host = Request.Host.Value;

        return $"{scheme}://{host}{trimmed}";
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }
}
