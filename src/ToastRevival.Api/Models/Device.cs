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

    // MachineGuid identity — COLLECTOR phase. Two stable machine signals the agent +
    // health service now report:
    //   MachineGuid : HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid — survives renames
    //                 AND the 15-char NetBIOS cap. The intended future identity key.
    //   DnsHostName : the FULL (non-truncated) primary hostname, for display + to settle
    //                 whether DeviceName is being truncated.
    // Both nullable: legacy rows and pre-collector agents carry null until an updated
    // agent/service reports. NOT yet used for device resolution — matching is still by
    // DeviceName — so storing these can never merge a row or move a seat. They are being
    // gathered to measure MachineGuid uniqueness across the fleet (the factory-clone
    // collision risk on OOTB mini-PCs) BEFORE any merge is designed.
    //
    // ANCHOR (collector phase): the write paths store these as the agent/service report
    // them (already normalized client-side by MachineIdentity.Normalize*). That is fine
    // while they are display/analysis-only. The FUTURE merge milestone MUST re-normalize
    // MachineGuid server-side before any equality match, or formatting variants from a
    // hand-crafted client could skew clone analysis. Tracked here so it isn't re-flagged.
    public string? MachineGuid { get; set; }
    public string? DnsHostName { get; set; }

    public string RegistrationToken { get; set; } = null!;
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public DateTime? LastPing { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<DeviceGroupMember> GroupMemberships { get; set; } = [];
    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
