namespace ToastRevival.Api.Models;

/// <summary>
/// XT-1 — a per-device, single-use, expiring, dashboard-issued enrollment token.
/// Replaces reliance on the reusable per-tenant <see cref="Tenant.EnrollmentKey"/>
/// for device registration. The opaque token is shown to the admin exactly once
/// at issue time; only its SHA-256 hash is stored here. The agent presents the
/// token in the same place it used to present the enrollment key (the HKLM
/// bootstrap value the MSI writes), so this needs no agent/installer change.
///
/// Security property (the XT-1 win): once a token is consumed it is bound to the
/// device identity that redeemed it, so a token left behind in a device's
/// registry is worthless to an attacker — it cannot provision a NEW rogue device.
/// </summary>
public class EnrollmentToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    /// <summary>SHA-256 (hex, lowercase) of the opaque token. Plaintext is never stored.</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>Optional admin-supplied label (e.g. "Reception PC").</summary>
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Unredeemed tokens are valid only until this instant (default 24h TTL).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Stamped on first successful registration. Once set, the token is bound to
    /// the device identity below so a clean reinstall of the SAME machine (the MSI
    /// wipes per-user config.json on uninstall) can still re-authenticate without
    /// minting a new seat. A different device presenting a used token is rejected.
    /// </summary>
    public DateTime? UsedAt { get; set; }
    public string? UsedByDeviceName { get; set; }
    public string? UsedByUsername { get; set; }

    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}
