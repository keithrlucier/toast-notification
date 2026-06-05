namespace ToastRevival.Api.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceName { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? OsVersion { get; set; }
    public string? AgentVersion { get; set; }

    // M1 (Device IP Capture) — WAN is server-derived on register/ping via
    // CloudflareIpValidator.ResolveTrustedClientIp (not spoofable by the agent);
    // LAN is agent-reported from NetworkUtils.GetLocalIPv4() (M2). Both nullable:
    // old agents/devices that predate the feature carry null until they re-ping.
    // 64 chars covers any IPv4 or full IPv6 (incl. zone ID).
    public string? WanIpAddress { get; set; }
    public string? LanIpAddress { get; set; }

    public string RegistrationToken { get; set; } = null!;
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public DateTime? LastPing { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<DeviceGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
