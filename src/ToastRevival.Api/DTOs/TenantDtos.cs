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
    // M9.C: per-tenant enrollment key surfaced to admin UI for the deploy command.
    // Returned to admins only — non-admins see null. Devices must POST this in
    // /api/devices/register when the tenant has a key set (INFO-M1-003).
    string? EnrollmentKey);

public class UpdateTenantSettingsRequest
{
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? DefaultAudioSetting { get; set; }
    public string? DefaultScenario { get; set; }
}

public record EnrollmentKeyResponse(string EnrollmentKey);
