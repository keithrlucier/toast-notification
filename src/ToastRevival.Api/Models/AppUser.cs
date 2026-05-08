using Microsoft.AspNetCore.Identity;

namespace ToastRevival.Api.Models;

public class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public UserRole Role { get; set; } = UserRole.Technician;
    public string? MfaSecret { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
