using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

public record RegisterDeviceRequest(
    [Required] Guid TenantId,
    // INJ-L1: MaxLength added to prevent oversized values from breaking DB write.
    [Required, MaxLength(256)] string DeviceName,
    [Required, MaxLength(256)] string Username,
    string? OsVersion = null,
    string? AgentVersion = null,
    string? EnrollmentKey = null,
    // M1 — agent-reported LAN IP. Optional with a default so an old agent that
    // omits it (every agent before M2) still deserializes cleanly — no 400.
    string? LanIpAddress = null,
    // MachineGuid identity (collector phase) — both optional/defaulted so a pre-collector
    // agent that omits them still deserializes. STORED only; matching is unchanged.
    [MaxLength(64)] string? MachineGuid = null,
    [MaxLength(256)] string? DnsHostName = null);

public record DeviceResponse(
    Guid DeviceId,
    string DeviceName,
    string Username,
    string? OsVersion,
    string? AgentVersion,
    string Status,
    DateTime? LastPing,
    DateTime RegisteredAt,
    IReadOnlyList<Guid> GroupIds,
    // M1 — WAN (server-derived) + LAN (agent-reported). Null for devices that
    // predate the feature; the dashboard renders a dash.
    string? WanIpAddress,
    string? LanIpAddress);

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

// Body for POST /api/agent/health/{tenantId} — sent by the SYSTEM-context
// ToastNotificationHealth service. MachineName matches Device.DeviceName
// (Environment.MachineName captured at registration). EnrollmentKey is only needed
// when the tenant uses the legacy reusable key. The tenant id travels in the route.
public record MachineHealthRequest(
    [Required, MaxLength(256)] string MachineName,
    string? EnrollmentKey = null,
    // MachineGuid identity (collector phase). Optional/defaulted so the 0.4.45 health
    // service (which sends only MachineName + EnrollmentKey) still deserializes. STORED
    // only — the endpoint still matches the machine by (tenant, MachineName).
    [MaxLength(64)] string? MachineGuid = null,
    [MaxLength(256)] string? DnsHostName = null);

// Body for POST /api/devices/ping. Optional — agents before 0.4.26 send no body.
// M1 — LanIpAddress optional with a default so a pre-M2 agent sending only
// { agentVersion } still deserializes; the server only overwrites a stored LAN
// when the incoming value is non-empty (never nulls a good value).
public record PingRequest(
    string? AgentVersion = null,
    string? LanIpAddress = null,
    // MachineGuid identity (collector phase) — optional/defaulted so a pre-collector
    // agent sending only { agentVersion } still deserializes. STORED only.
    [MaxLength(64)] string? MachineGuid = null,
    [MaxLength(256)] string? DnsHostName = null);
