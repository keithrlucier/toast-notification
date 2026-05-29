using Microsoft.AspNetCore.Identity;

namespace ToastRevival.Api.Models;

public enum RegistrationStep
{
    PendingSmsVerification = 0,
    PendingPasswordSet = 1,
    Complete = 2,
}

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public UserRole Role { get; set; } = UserRole.Technician;
    public bool IsPlatformAdmin { get; set; }
    public string? MfaSecret { get; set; }

    // Last TOTP time-step accepted by MfaService.Verify. RFC 6238 step =
    // floor(unixSeconds / 30). Verify rejects any code whose matched step is
    // <= this value, blocking replay within (and slightly before) the same
    // 30s step. Null until the user's first successful MFA.
    public long? LastTotpStep { get; set; }

    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Registration flow fields
    public string? FullName { get; set; }
    public string? SmsVerificationCode { get; set; }   // SHA-256 hashed 6-digit code
    public DateTime? SmsCodeExpiry { get; set; }
    public RegistrationStep RegistrationStep { get; set; } = RegistrationStep.Complete;

    // ─── External identity (Microsoft SSO) ───────────────────────────────────
    // Set when a user links a federated identity. Link-only model: a federated
    // sign-in maps to an EXISTING user by verified email within the mapped
    // tenant; on first successful federated sign-in we record the provider plus
    // the stable subject (Entra "oid") so subsequent sign-ins match on the
    // immutable object id, not the mutable email/UPN.
    public string? ExternalProvider { get; set; }   // e.g. "microsoft"
    public string? ExternalId { get; set; }          // Entra object id (oid claim)

    public Tenant Tenant { get; set; } = null!;
}
