namespace ToastRevival.Api.DTOs;

public record TenantSettingsResponse(
    string TenantName,
    string? LogoUrl,
    string? PrimaryColor,
    string? DefaultAudioSetting,
    string DefaultScenario,
    int RateLimitPerMinute,
    int RateLimitPerHour,
    int RateLimitPerDay,
    // Per-tenant enrollment key surfaced to admin UI for the deploy command.
    // Returned to admins only — non-admins see null. Devices must POST this
    // in /api/devices/register when the tenant has a key set.
    string? EnrollmentKey);

public class UpdateTenantSettingsRequest
{
    public string? TenantName { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? DefaultAudioSetting { get; set; }
    public string? DefaultScenario { get; set; }
}

public record EnrollmentKeyResponse(string EnrollmentKey);

/// <summary>
/// M11 — per-tenant content moderation policy.
///
/// Severity values follow the Azure Content Safety scale (0..6). The pre-M11
/// platform defaults were ReviewSeverity=2, BlockSeverity=5; the row stores the
/// tenant's overrides. CustomEndpoint+CustomKey are returned with the key masked
/// (last 4 chars only) — the raw key is never sent back to the dashboard once
/// stored.
/// </summary>
public record TenantModerationSettingsResponse(
    bool Enabled,
    bool ScanText,
    bool ScanImages,
    int ReviewSeverity,
    int BlockSeverity,
    bool RequireApprovalAll,
    string? CustomEndpoint,
    string? CustomKeyMasked,   // null when no key set; otherwise "••••••••<last4>"
    string? BlockedMessage,
    // Platform-default endpoint visibility — read-only, surfaced so the UI can show
    // "Using platform default Azure Content Safety endpoint" vs "Custom".
    bool PlatformEndpointConfigured);

public class UpdateTenantModerationSettingsRequest
{
    public bool Enabled { get; set; } = true;
    public bool ScanText { get; set; } = true;
    public bool ScanImages { get; set; } = true;
    public int ReviewSeverity { get; set; } = 2;
    public int BlockSeverity { get; set; } = 5;
    public bool RequireApprovalAll { get; set; }
    public string? CustomEndpoint { get; set; }
    /// <summary>
    /// Send the new raw key to set/rotate. Send null/empty to leave existing key
    /// unchanged. Send the sentinel "__clear__" to remove the stored custom key
    /// and revert to the platform default.
    /// </summary>
    public string? CustomKey { get; set; }
    public string? BlockedMessage { get; set; }
}
