namespace ToastRevival.Api.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceName { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? OsVersion { get; set; }
    public string? AgentVersion { get; set; }
    public string RegistrationToken { get; set; } = null!;
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public DateTime? LastPing { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<DeviceGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
