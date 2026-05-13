namespace ToastRevival.Api.Models;

public class DeviceGroupMember
{
    public Guid DeviceGroupId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public DeviceGroup DeviceGroup { get; set; } = null!;
    public Device Device { get; set; } = null!;
}
