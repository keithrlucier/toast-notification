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
    DateTime RegisteredAt,
    IReadOnlyList<Guid> GroupIds);

public record DeviceTokenResponse(
    string Token,
    Guid DeviceId,
    Guid TenantId,
    string SigningKey,
    string TenantName);

public record InteractionRequest(
    [Required, MaxLength(64)] string Action);

// Returned by GET /api/devices/tenant-name (device-JWT). LogoUrl is the tenant's
// configured notification icon — the agent downloads it on startup and writes
// the local path as the AUMID IconUri (the tiny attribution icon at the top of
// every Windows toast). Optional with a default so an old 0.4.5 agent that
// only reads TenantName continues to deserialize cleanly.
public record TenantAttributionResponse(string TenantName, string? LogoUrl = null);

// Body for POST /api/devices/ping. Optional — agents before 0.4.26 send no body.
public record PingRequest(string? AgentVersion = null);
