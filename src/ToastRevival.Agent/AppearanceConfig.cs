namespace ToastRevival.Agent;

/// <summary>
/// M12 — agent-side wire shape of GET /api/devices/appearance-config. Mirrors the
/// API's AppearanceConfigResponse. Every member is null-tolerant so a sparse or
/// older-server response deserializes cleanly; the services apply defaults.
/// (ReadFromJsonAsync uses Web defaults — case-insensitive — so PascalCase here
/// binds the camelCase JSON, same as TenantAttributionDto.)
/// </summary>
internal sealed record AppearanceConfig(
    OverlayConfig? Overlay,
    LockScreenConfig? LockScreen);

/// <summary>
/// Desktop info-overlay config. <see cref="Fields"/> holds the enabled field keys
/// (canonical: hostname | user | os | ip | tenant | customtext). <see cref="Position"/>
/// is one of bottom-right | bottom-left | top-right | top-left.
/// </summary>
internal sealed record OverlayConfig(
    bool Enabled,
    string[]? Fields,
    string? Position,
    string? CustomText);

/// <summary>Lock screen branding config. ImageUrl is an absolute http(s) URL.</summary>
internal sealed record LockScreenConfig(
    bool Enabled,
    string? ImageUrl);
