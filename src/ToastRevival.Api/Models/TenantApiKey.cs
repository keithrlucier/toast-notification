namespace ToastRevival.Api.Models;

public class TenantApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string KeyPrefix { get; set; } = null!;  // first 8 chars, shown in UI
    public string KeyHash { get; set; } = null!;    // SHA-256 of full key (hex)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
