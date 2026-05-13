namespace ToastRevival.Api.Models;

public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid TenantId { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public DateTime? DeliveredAt { get; set; }
    public DateTime? InteractedAt { get; set; }
    public string? Action { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Notification Notification { get; set; } = null!;
    public Device Device { get; set; } = null!;
}
