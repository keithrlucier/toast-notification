namespace ToastRevival.Api.DTOs;

public record TenantSettingsResponse(
    string TenantName,
    string? LogoUrl,
    string? PrimaryColor,
    string? DefaultAudioSetting,
    string DefaultScenario,
    int RateLimitPerMinute,
    int RateLimitPerHour,
    int RateLimitPerDay);

public class UpdateTenantSettingsRequest
{
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? DefaultAudioSetting { get; set; }
    public string? DefaultScenario { get; set; }
}
