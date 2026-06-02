using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

/// <summary>
/// M12 — single source of truth for the device-appearance vocabulary and the
/// Tenant→DTO mapping. Both the admin tenant endpoints and the agent-facing
/// device endpoint go through here so the overlay field set, the position
/// vocabulary, and the persisted-row → response shape can never diverge.
///
/// The canonical overlay field keys below are the contract the agent's
/// DesktopOverlayService resolves and the dashboard checkboxes write. Public
/// marketing/LLM copy that enumerates "what the overlay shows" must match this
/// set (crawler-drift standing rule).
/// </summary>
public static class TenantAppearance
{
    public const string DefaultPosition = "bottom-right";

    /// <summary>Quadrant keys the overlay window understands.</summary>
    public static readonly IReadOnlySet<string> Positions =
        new HashSet<string> { "bottom-right", "bottom-left", "top-right", "top-left" };

    /// <summary>Canonical overlay field keys (lowercase). "customtext" is membership
    /// in the set; the literal string lives in Tenant.DesktopOverlayCustomText.</summary>
    public static readonly IReadOnlySet<string> AllowedFields =
        new HashSet<string> { "hostname", "user", "os", "ip", "tenant", "customtext" };

    /// <summary>Pipe-delimited stored value → clean, validated, de-duplicated key array.</summary>
    public static string[] SplitFields(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];
        return stored
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToLowerInvariant())
            .Where(AllowedFields.Contains)
            .Distinct()
            .ToArray();
    }

    /// <summary>Request key array → pipe-delimited stored value (null when empty).
    /// Unknown keys are dropped at the boundary so only the canonical set persists.</summary>
    public static string? JoinFields(string[]? fields)
    {
        if (fields is null || fields.Length == 0) return null;
        var clean = fields
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToLowerInvariant())
            .Where(AllowedFields.Contains)
            .Distinct()
            .ToArray();
        return clean.Length == 0 ? null : string.Join('|', clean);
    }

    /// <summary>Normalizes any stored/requested position to a valid quadrant,
    /// defaulting to bottom-right. Used by the agent and the admin GET alike.</summary>
    public static string NormalizePosition(string? position)
    {
        var v = position?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(v) && Positions.Contains(v) ? v : DefaultPosition;
    }

    public static OverlayConfigResponse BuildOverlay(Tenant t) => new(
        Enabled:        t.DesktopOverlayEnabled,
        Fields:         SplitFields(t.DesktopOverlayFields),
        Position:       NormalizePosition(t.DesktopOverlayPosition),
        CustomText:     t.DesktopOverlayCustomText,
        OpacityPercent: NormalizeOpacity(t.DesktopOverlayOpacityPercent));

    /// <summary>
    /// Clamps and snaps the stored opacity to a value the agent renders cleanly:
    /// 10..100 inclusive, snapped to the nearest 5% step. Acts on inbound
    /// requests AND outbound responses so a hand-edited DB row can't push a
    /// junk value out to the agent.
    /// </summary>
    public static int NormalizeOpacity(int raw)
    {
        var clamped = Math.Clamp(raw, 10, 100);
        var snapped = (int)Math.Round(clamped / 5.0) * 5;
        return Math.Clamp(snapped, 10, 100);
    }

    public static LockScreenConfigResponse BuildLockScreen(Tenant t) => new(
        Enabled:           t.LockScreenEnabled,
        ImageUrl:          t.LockScreenImageUrl,
        ImageUpdatedAtUtc: t.LockScreenImageUpdatedAt);
}
