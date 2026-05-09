using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

public record DeviceGroupResponse(
    Guid Id,
    string Name,
    string? Description,
    int DeviceCount,
    DateTime CreatedAt);

public record CreateDeviceGroupRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(500)] string? Description);

public record AddMemberRequest([Required] Guid DeviceId);

public record DeviceGroupMemberResponse(
    Guid DeviceId,
    string DeviceName,
    string? AgentVersion,
    DateTime AddedAt);
