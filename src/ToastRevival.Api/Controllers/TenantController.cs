using System.Security.Claims;
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
using ToastRevival.Api.Utilities;

namespace ToastRevival.Api.Controllers;

// REVIEW-2026-06-06 ARCH-M4 REJECTED-by-design: 607-line TenantController is a known size issue; split is planned for a dedicated refactor milestone

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class TenantController : ControllerBase
{
    private static readonly HashSet<string> AllowedLogoExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    private const long MaxLogoSizeBytes = 2 * 1024 * 1024;

    // M12 lock screen image — JPG/PNG only (the formats Windows lock screen and a
    // dashboard <img> both render cleanly under X-Content-Type-Options: nosniff).
    private static readonly HashSet<string> AllowedLockScreenExtensions =
        [".jpg", ".jpeg", ".png"];

    private const long MaxLockScreenImageBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<NotificationHub> _hub;

    public TenantController(AppDbContext db, IWebHostEnvironment env, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _env = env;
        _hub = hub;
    }

    /// <summary>
    /// M12.B — tell every connected device in this tenant to re-fetch and re-apply
    /// its appearance config now (instead of waiting for the next agent restart).
    /// The agent's AppearanceUpdated handler covers both apply and revert-on-disable.
    /// Devices and dashboard clients share the tenant-{id} group; the dashboard
    /// ignores this message. Best-effort — a SignalR failure must not fail the save.
    /// </summary>
    private async Task NotifyAppearanceChangedAsync(Guid tenantId)
    {
        try { await _hub.Clients.Group($"tenant-{tenantId}").SendAsync("AppearanceUpdated"); }
        catch { /* best-effort: the device still catches up at next startup */ }
    }

    [HttpGet("settings")]
    public async Task<ActionResult<TenantSettingsResponse>> GetSettings()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        // EnrollmentKey only surfaces to admins — Technicians get null. The
        // key must be paste-able into RMM/Intune deploy scripts but shouldn't
        // show in the read-only settings view of a junior tech.
        var enrollmentKey = IsAdmin() ? tenant.EnrollmentKey : null;

        return Ok(new TenantSettingsResponse(
            TenantName:          tenant.Name,
            LogoUrl:             tenant.LogoUrl,
            PrimaryColor:        tenant.PrimaryColor,
            DefaultAudioSetting: tenant.DefaultAudioSetting,
            DefaultScenario:     tenant.DefaultScenario.ToString(),
            RateLimitPerMinute:  60,
            RateLimitPerHour:    500,
            RateLimitPerDay:     5000,
            EnrollmentKey:       enrollmentKey));
    }

    /// <summary>
    /// Admin-only regenerate of the per-tenant enrollment key. Returns the
    /// new key. After rotation, every existing v0.3.2+ agent install continues
    /// to work (the key is only checked at /api/devices/register, not on the
    /// device JWT it already holds), but any new MSI deploy must use the new
    /// key — old MSIs with the prior key in their bootstrap will be 403'd.
    /// </summary>
    [HttpPost("enrollment-key/regenerate")]
    public async Task<ActionResult<EnrollmentKeyResponse>> RegenerateEnrollmentKey()
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        tenant.EnrollmentKey = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        await _db.SaveChangesAsync();

        return Ok(new EnrollmentKeyResponse(tenant.EnrollmentKey));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateTenantSettingsRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.TenantName))
            tenant.Name = req.TenantName.Trim();
        tenant.LogoUrl             = NormalizeLogoUrlForStorage(req.LogoUrl);
        tenant.PrimaryColor        = string.IsNullOrWhiteSpace(req.PrimaryColor)        ? null : req.PrimaryColor.Trim();
        tenant.DefaultAudioSetting = string.IsNullOrWhiteSpace(req.DefaultAudioSetting) ? null : req.DefaultAudioSetting;

        if (req.DefaultScenario is not null
            && Enum.TryParse<ToastScenario>(req.DefaultScenario, ignoreCase: true, out var scenario))
            tenant.DefaultScenario = scenario;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Tenant-wide native MFA enforcement ───────────────────────────────────
    // When on: every member must enroll an authenticator, and sending a toast /
    // changing the lock screen require a fresh step-up verification. Admin-only.

    [HttpGet("mfa-policy")]
    public async Task<ActionResult<TenantMfaPolicyResponse>> GetMfaPolicy()
    {
        if (!IsAdmin()) return Forbid();
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        var callerEnrolled = await CallerHasMfaAsync();
        return Ok(new TenantMfaPolicyResponse(t.RequireMfa, callerEnrolled));
    }

    [HttpPut("mfa-policy")]
    public async Task<IActionResult> UpdateMfaPolicy([FromBody] UpdateTenantMfaPolicyRequest req)
    {
        if (!IsAdmin()) return Forbid();
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        // Self-lockout guard: an admin cannot enforce MFA workspace-wide until their
        // own account has a confirmed authenticator — otherwise the person flipping
        // the switch is the first one locked out of sensitive actions.
        if (req.RequireMfa && !t.RequireMfa && !await CallerHasMfaAsync())
            return BadRequest(new
            {
                message = "Set up your own authenticator (Security → Two-Factor Authentication) before requiring MFA for the workspace."
            });

        t.RequireMfa = req.RequireMfa;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>True when the calling user has a confirmed authenticator (MfaSecret set).</summary>
    private async Task<bool> CallerHasMfaAsync()
    {
        var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var secret = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == uid)
            .Select(u => u.MfaSecret)
            .FirstOrDefaultAsync();
        return !string.IsNullOrWhiteSpace(secret);
    }

    /// <summary>
    /// Step-up gate for sensitive tenant actions. When the tenant enforces MFA and
    /// the caller's token isn't elevated (mfa=true claim), returns a 403 the frontend
    /// turns into an MFA prompt. Returns null when the action may proceed. Shares the
    /// mfa=true mechanism with broadcast-to-all in NotificationsController.
    /// </summary>
    private IActionResult? RequireStepUpMfa(bool tenantRequiresMfa, string action, IConfiguration config)
    {
        if (tenantRequiresMfa && !User.HasFreshMfa(config))
            return StatusCode(403, new
            {
                error = "mfa_required",
                message = $"{action} requires MFA verification. Verify your authenticator and try again."
            });
        return null;
    }

    /// <summary>
    /// Uploads a logo image for the tenant and returns its public URL.
    /// The URL is stored in Tenant.LogoUrl and used as the notification icon.
    /// </summary>
    [HttpPost("logo")]
    [RequestSizeLimit(2 * 1024 * 1024 + 4096)]
    public async Task<ActionResult<object>> UploadLogo(IFormFile file)
    {
        if (!IsAdmin()) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
        if (file.Length > MaxLogoSizeBytes) return BadRequest(new { message = "Logo must be under 2 MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedLogoExtensions.Contains(ext))
            return BadRequest(new { message = "Unsupported file type. Use PNG, JPG, GIF, or WebP." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var webRoot  = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir      = Path.Combine(webRoot, "assets", "logos");
        Directory.CreateDirectory(dir);

        var fileName = $"{tenantId}{ext}";
        var filePath = Path.Combine(dir, fileName);

        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        var url = $"/assets/logos/{fileName}";

        // Persist the URL to the tenant record immediately
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is not null)
        {
            tenant.LogoUrl = url;
            await _db.SaveChangesAsync();
        }

        return Ok(new { url });
    }

    /// <summary>
    /// M11 — per-tenant content moderation policy. Admin+ only.
    /// Custom Azure Content Safety key is returned masked (last 4 chars only);
    /// the dashboard never receives the raw key after first save.
    /// </summary>
    [HttpGet("moderation")]
    public async Task<ActionResult<TenantModerationSettingsResponse>> GetModeration(
        [FromServices] IConfiguration config)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        var platformConfigured =
            !string.IsNullOrWhiteSpace(config["ContentSafety:Endpoint"]) &&
            !string.IsNullOrWhiteSpace(config["ContentSafety:Key"]);

        return Ok(new TenantModerationSettingsResponse(
            Enabled:                     t.ModerationEnabled,
            ScanText:                    t.ModerationScanText,
            ScanImages:                  t.ModerationScanImages,
            ReviewSeverity:              t.ModerationReviewSeverity,
            BlockSeverity:               t.ModerationBlockSeverity,
            RequireApprovalAll:          t.ModerationRequireApprovalAll,
            CustomEndpoint:              t.ModerationCustomEndpoint,
            CustomKeyMasked:             MaskKey(t.ModerationCustomKey),
            BlockedMessage:              t.ModerationBlockedMessage,
            PlatformEndpointConfigured:  platformConfigured));
    }

    [HttpPut("moderation")]
    public async Task<IActionResult> UpdateModeration([FromBody] UpdateTenantModerationSettingsRequest req)
    {
        if (!IsAdmin()) return Forbid();

        // Validate severity windows on the Azure Content Safety 0..6 scale and the
        // invariant that BlockSeverity > ReviewSeverity (otherwise everything that
        // would Review immediately Blocks instead).
        if (req.ReviewSeverity < 0 || req.ReviewSeverity > 6)
            return BadRequest("ReviewSeverity must be between 0 and 6.");
        if (req.BlockSeverity < 0 || req.BlockSeverity > 6)
            return BadRequest("BlockSeverity must be between 0 and 6.");
        if (req.BlockSeverity <= req.ReviewSeverity)
            return BadRequest("BlockSeverity must be greater than ReviewSeverity.");

        // Custom endpoint must be a valid HTTPS URL when set
        if (!string.IsNullOrWhiteSpace(req.CustomEndpoint))
        {
            if (!Uri.TryCreate(req.CustomEndpoint, UriKind.Absolute, out var parsed)
                || parsed.Scheme != "https")
            {
                return BadRequest("CustomEndpoint must be an absolute https:// URL.");
            }
        }

        if (req.BlockedMessage is { Length: > 500 })
            return BadRequest("BlockedMessage must be 500 characters or fewer.");

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        t.ModerationEnabled            = req.Enabled;
        t.ModerationScanText           = req.ScanText;
        t.ModerationScanImages         = req.ScanImages;
        t.ModerationReviewSeverity     = req.ReviewSeverity;
        t.ModerationBlockSeverity      = req.BlockSeverity;
        t.ModerationRequireApprovalAll = req.RequireApprovalAll;
        t.ModerationCustomEndpoint     = string.IsNullOrWhiteSpace(req.CustomEndpoint)
            ? null
            : req.CustomEndpoint.Trim();
        t.ModerationBlockedMessage     = string.IsNullOrWhiteSpace(req.BlockedMessage)
            ? null
            : req.BlockedMessage.Trim();

        // Key handling: null/empty = leave unchanged; "__clear__" = remove; anything else = replace.
        if (req.CustomKey == "__clear__")
            t.ModerationCustomKey = null;
        else if (!string.IsNullOrWhiteSpace(req.CustomKey))
            t.ModerationCustomKey = req.CustomKey.Trim();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── M12 Device Appearance ───────────────────────────────────────────────
    // Desktop overlay + lock screen branding. GETs are readable by any
    // authenticated tenant user (same as GetSettings — the config carries no
    // secrets and the page must render for Technicians); mutations are IsAdmin().

    /// <summary>Desktop info-overlay config for the current tenant.</summary>
    [HttpGet("overlay")]
    public async Task<ActionResult<OverlayConfigResponse>> GetOverlay()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();
        return Ok(TenantAppearance.BuildOverlay(t));
    }

    [HttpPut("overlay")]
    public async Task<IActionResult> UpdateOverlay(
        [FromBody] UpdateOverlayConfigRequest req,
        [FromServices] IConfiguration config)
    {
        if (!IsAdmin()) return Forbid();

        // Position must be one of the four quadrant keys when supplied.
        if (!string.IsNullOrWhiteSpace(req.Position)
            && !TenantAppearance.Positions.Contains(req.Position.Trim().ToLowerInvariant()))
            return BadRequest(new { message = "Position must be bottom-right, bottom-left, top-right, or top-left." });

        var customText = string.IsNullOrWhiteSpace(req.CustomText) ? null : req.CustomText.Trim();
        if (customText is { Length: > 80 })
            return BadRequest(new { message = "Custom text must be 80 characters or fewer." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        // FIX-MFA-1 (2026-06-01): the overlay is a fleet-wide appearance write —
        // it broadcasts AppearanceUpdated to every online agent below, identical
        // threat surface to the lock-screen. It was the un-gated sibling M15 left
        // behind (UpdateLockScreen/UploadLockScreenImage already step-up). Gate it
        // on the same shared helper so all three appearance writes are consistent.
        var gate = RequireStepUpMfa(t.RequireMfa, "Changing the desktop overlay", config);
        if (gate is not null) return gate;

        t.DesktopOverlayEnabled        = req.Enabled;
        t.DesktopOverlayFields         = TenantAppearance.JoinFields(req.Fields);
        t.DesktopOverlayPosition       = TenantAppearance.NormalizePosition(req.Position);
        t.DesktopOverlayCustomText     = customText;
        // OpacityPercent normalizes/snaps to 5% steps in [10,100]; an absent
        // field on inbound request keeps the existing stored value (don't
        // clobber to default 85 on an upsert that didn't touch this control).
        if (req.OpacityPercent is int op)
            t.DesktopOverlayOpacityPercent = TenantAppearance.NormalizeOpacity(op);
        await _db.SaveChangesAsync();
        await NotifyAppearanceChangedAsync(tenantId);
        return NoContent();
    }

    /// <summary>Lock screen branding config for the current tenant.</summary>
    [HttpGet("lockscreen")]
    public async Task<ActionResult<LockScreenConfigResponse>> GetLockScreen()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();
        return Ok(TenantAppearance.BuildLockScreen(t));
    }

    [HttpPut("lockscreen")]
    public async Task<IActionResult> UpdateLockScreen(
        [FromBody] UpdateLockScreenConfigRequest req,
        [FromServices] IConfiguration config)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        var gate = RequireStepUpMfa(t.RequireMfa, "Changing the lock screen", config);
        if (gate is not null) return gate;

        t.LockScreenEnabled = req.Enabled;
        // Save also persists the image (or clears it on Remove), mirroring the
        // logo + settings split. Constrained to our own /assets/lockscreen/ path —
        // never an arbitrary URL the agent would then fetch.
        t.LockScreenImageUrl = NormalizeLockScreenUrlForStorage(req.ImageUrl);
        t.LockScreenImageUpdatedAt = DateTime.UtcNow; // DASH-L1 cache-bust
        await _db.SaveChangesAsync();
        await NotifyAppearanceChangedAsync(tenantId);
        return NoContent();
    }

    /// <summary>
    /// Uploads the per-tenant lock screen image and returns its public URL.
    /// Persists to the same redeploy-surviving assets root as AssetsController
    /// (Assets:RootPath), NOT the deploy-dir webroot the logo upload uses — the
    /// agent must still find this image after the next deploy. Extension + byte
    /// size validation only; Windows handles dimension fit on the device.
    /// </summary>
    [HttpPost("lockscreen-image")]
    [RequestSizeLimit(5 * 1024 * 1024 + 4096)]
    public async Task<ActionResult<object>> UploadLockScreenImage(
        IFormFile file, [FromServices] IConfiguration config)
    {
        if (!IsAdmin()) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });
        if (file.Length > MaxLockScreenImageBytes) return BadRequest(new { message = "Image must be under 5 MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedLockScreenExtensions.Contains(ext))
            return BadRequest(new { message = "Unsupported file type. Use JPG or PNG." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        // Step-up MFA gate BEFORE any disk write — reject an unverified upload
        // instead of writing the file and then 403'ing. Inlined (not the shared
        // helper) because this action returns ActionResult<object>, which doesn't
        // implicitly convert from the helper's IActionResult.
        var requireMfa = await _db.Tenants.IgnoreQueryFilters()
            .Where(x => x.Id == tenantId).Select(x => x.RequireMfa).FirstOrDefaultAsync();
        if (requireMfa && !User.HasFreshMfa(config))
            return StatusCode(403, new
            {
                error = "mfa_required",
                message = "Changing the lock screen requires MFA verification. Verify your authenticator and try again."
            });

        // Same persistent-root resolution as AssetsController so the file lands
        // on /opt/toast/shared/assets in prod and is served at /assets.
        var webRoot    = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var assetsRoot = config["Assets:RootPath"] ?? Path.Combine(webRoot, "assets");
        var dir        = Path.Combine(assetsRoot, "lockscreen");
        Directory.CreateDirectory(dir);

        // Drop any prior image for this tenant (possibly a different extension) so a
        // JPG→PNG swap doesn't orphan the old file or leave a stale URL resolving.
        foreach (var existing in Directory.EnumerateFiles(dir)
                     .Where(p => Path.GetFileNameWithoutExtension(p)
                         .Equals(tenantId.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            try { System.IO.File.Delete(existing); } catch { /* best-effort cleanup */ }
        }

        var fileName = $"{tenantId}{ext}";
        var filePath = Path.Combine(dir, fileName);
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        // Relative path (logo-style). The device endpoint absolutizes it via
        // ToPublicUrl for the agent, and the dashboard loads it same-origin. Storing
        // relative also lets UpdateLockScreen constrain the value to our own assets
        // path so an admin can't repoint fleet lock screens at an arbitrary URL.
        var url = $"/assets/lockscreen/{fileName}";

        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is not null)
        {
            tenant.LockScreenImageUrl = url;
            tenant.LockScreenImageUpdatedAt = DateTime.UtcNow; // DASH-L1 cache-bust
            await _db.SaveChangesAsync();
            // Image commits immediately (Diana's split: upload now, Save toggles
            // enabled). Refresh live so a re-upload-while-enabled reaches devices.
            await NotifyAppearanceChangedAsync(tenantId);
        }

        return Ok(new { url });
    }

    // ── M14 Microsoft SSO (per-tenant directory mapping) ─────────────────────
    // Platform owns the Entra app credentials (System SSO config). Each tenant
    // opts in by mapping its own Entra Directory (tenant) ID here. Admin-only —
    // the directory id is org-identifying and the toggle changes who can log in.

    [HttpGet("sso")]
    public async Task<ActionResult<TenantSsoSettingsResponse>> GetSso([FromServices] IConfiguration config)
    {
        if (!IsAdmin()) return Forbid();
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        var platformConfigured =
            config.GetValue<bool>("Sso:Microsoft:Enabled")
            && !string.IsNullOrWhiteSpace(config["Sso:Microsoft:ClientId"])
            && !string.IsNullOrWhiteSpace(config["Sso:Microsoft:ClientSecret"]);

        return Ok(new TenantSsoSettingsResponse(
            Enabled:            t.SsoEnabled,
            AzureAdTenantId:    t.AzureAdTenantId,
            RequireMfa:         t.SsoRequireMfa,
            PlatformConfigured: platformConfigured,
            MicrosoftClientId:  config["Sso:Microsoft:ClientId"]));
    }

    [HttpPut("sso")]
    public async Task<IActionResult> UpdateSso([FromBody] UpdateTenantSsoSettingsRequest req)
    {
        if (!IsAdmin()) return Forbid();
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var t = await _db.Tenants.FindAsync(tenantId);
        if (t is null) return NotFound();

        string? dirId = null;
        if (!string.IsNullOrWhiteSpace(req.AzureAdTenantId))
        {
            if (!Guid.TryParse(req.AzureAdTenantId.Trim(), out var parsed))
                return BadRequest(new { message = "Directory (tenant) ID must be a valid GUID." });
            dirId = parsed.ToString();   // canonical lowercase, hyphenated form
        }

        if (req.Enabled && dirId is null)
            return BadRequest(new { message = "A Directory (tenant) ID is required to enable Microsoft sign-in." });

        // One directory maps to exactly one tenant — otherwise the SSO callback's
        // tenant lookup is ambiguous. Refuse a directory already claimed elsewhere.
        if (dirId is not null)
        {
            var clash = await _db.Tenants.IgnoreQueryFilters()
                .AnyAsync(x => x.Id != t.Id && x.AzureAdTenantId == dirId);
            if (clash)
                return Conflict(new { message = "That Microsoft directory is already linked to another tenant." });
        }

        t.AzureAdTenantId = dirId;
        t.SsoEnabled      = req.Enabled && dirId is not null;
        t.SsoRequireMfa   = req.RequireMfa;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string? MaskKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Length <= 4) return new string('•', key.Length);
        return new string('•', 8) + key[^4..];
    }

    // ARCH-L4: Extract shared body into NormalizeAssetUrlForStorage.
    // CFG-4: accept only our own asset paths. An external URL stores null to prevent SSRF.
    private static string? NormalizeAssetUrlForStorage(string? url, string prefix)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return uri.PathAndQuery;
        }

        return null;
    }

    private static string? NormalizeLogoUrlForStorage(string? logoUrl) =>
        NormalizeAssetUrlForStorage(logoUrl, "/assets/logos/");

    private static string? NormalizeLockScreenUrlForStorage(string? imageUrl) =>
        NormalizeAssetUrlForStorage(imageUrl, "/assets/lockscreen/");

    // ARCH-M1: Delegates to the shared ClaimsPrincipalExtensions.IsAdmin().
    private bool IsAdmin() => User.IsAdmin();
}
