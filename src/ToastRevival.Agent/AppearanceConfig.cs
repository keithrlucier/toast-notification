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
    LockScreenConfig? LockScreen,
    // AGT-4-R: present on signed responses — the canonical {overlay, lockScreen} JSON the
    // server HMAC'd and the base64 signature. The agent verifies the HMAC over SignedPayload
    // and applies the config parsed FROM those exact bytes (see AgentClient).
    string? SignedPayload = null,
    string? Signature = null);

/// <summary>
/// Desktop info-overlay config. <see cref="Fields"/> holds the enabled field keys
/// (canonical: hostname | user | os | ip | tenant | customtext). <see cref="Position"/>
/// is one of bottom-right | bottom-left | top-right | top-left.
/// <see cref="OpacityPercent"/> is the panel translucency 10–100 (in 5% steps).
/// Nullable so a pre-0.4.15 server that omits the field defaults agent-side to 85.
/// </summary>
internal sealed record OverlayConfig(
    bool Enabled,
    string[]? Fields,
    string? Position,
    string? CustomText,
    int? OpacityPercent);

/// <summary>Lock screen branding config. ImageUrl is an absolute http(s) URL.
/// ImageUpdatedAtUtc (DASH-L1) is the server cache-bust stamp; nullable for older servers.</summary>
internal sealed record LockScreenConfig(
    bool Enabled,
    string? ImageUrl,
    DateTime? ImageUpdatedAtUtc = null);
