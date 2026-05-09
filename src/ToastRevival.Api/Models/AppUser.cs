using Microsoft.AspNetCore.Identity;

namespace ToastRevival.Api.Models;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public UserRole Role { get; set; } = UserRole.Technician;
    public bool IsPlatformAdmin { get; set; }
    public string? MfaSecret { get; set; }

    // SEC-005 / INFO-M3-001: last TOTP time-step accepted by MfaService.Verify.
    // RFC 6238 step = floor(unixSeconds / 30). Verify rejects any code whose
    // matched step is <= this value, blocking replay within (and slightly
    // before) the same 30s step. Null until the user's first successful MFA.
    public long? LastTotpStep { get; set; }

    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
