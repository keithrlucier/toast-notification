namespace ToastRevival.Api.Models;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public string Subdomain { get; set; } = null!;

    // Per-tenant HMAC-SHA256 signing key (base64). Generated at tenant creation,
    // returned to agents at device registration so they can verify notification
    // payloads before rendering. Rotation is M3 work.
    public string SigningKey { get; set; } = null!;

    public int LicenseCount { get; set; } = 10;
    public int ConsumedCount { get; set; }
    public DateTime? LicenseStart { get; set; }
    public DateTime? LicenseEnd { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;
    public BillingStatus BillingStatus { get; set; } = BillingStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<DeviceGroup> DeviceGroups { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
