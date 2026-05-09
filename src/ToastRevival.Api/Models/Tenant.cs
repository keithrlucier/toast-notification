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

    // Optional pre-shared key required for device registration (INFO-M1-003).
    // When null, device registration is open — any caller that knows the TenantId
    // may register. When set, the agent must include the matching key in its
    // RegisterDeviceRequest or registration is rejected.
    public string? EnrollmentKey { get; set; }

    public int LicenseCount { get; set; }
    public int ConsumedCount { get; set; }
    public DateTime? LicenseStart { get; set; }
    public DateTime? LicenseEnd { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Standard;
    public BillingStatus BillingStatus { get; set; } = BillingStatus.Active;
    // Stripe billing (M6)
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? PastDueAt { get; set; }

    // Branding & notification defaults (M5.B D3)
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? DefaultAudioSetting { get; set; }
    public ToastScenario DefaultScenario { get; set; } = ToastScenario.Default;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<DeviceGroup> DeviceGroups { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
