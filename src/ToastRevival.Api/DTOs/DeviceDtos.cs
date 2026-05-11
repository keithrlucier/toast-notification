using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

public record RegisterDeviceRequest(
    [Required] Guid TenantId,
    [Required] string DeviceName,
    [Required] string Username,
    string? OsVersion = null,
    string? AgentVersion = null,
    string? EnrollmentKey = null);

public record DeviceResponse(
    Guid DeviceId,
    string DeviceName,
    string Username,
    string? OsVersion,
    string? AgentVersion,
    string Status,
    DateTime? LastPing,
    DateTime RegisteredAt);

public record DeviceTokenResponse(
    string Token,
    Guid DeviceId,
    Guid TenantId,
    string SigningKey,
    string TenantName);

public record InteractionRequest(
    [Required, MaxLength(64)] string Action);
