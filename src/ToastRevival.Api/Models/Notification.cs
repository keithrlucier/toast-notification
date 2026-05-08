namespace ToastRevival.Api.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid SenderId { get; set; }
    public string Title { get; set; } = null!;
    public string? BodyLine1 { get; set; }
    public string? BodyLine2 { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? ActionButtonsJson { get; set; }
    public string? AudioSetting { get; set; }
    public ToastScenario Scenario { get; set; } = ToastScenario.Default;
    public TargetType TargetType { get; set; }
    public string? TargetIdsJson { get; set; }
    public int TargetDeviceCount { get; set; }
    public string? ModerationResultJson { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public AppUser Sender { get; set; } = null!;
    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
