using System.Text.Json.Serialization;

namespace ToastRevival.AgentHealthService;

/// <summary>
/// Machine-level identity read from HKLM each tick. The TenantId + ServerUrl come
/// from the same registry the agent reads (DeviceConfig.TryLoadBootstrapFromRegistry);
/// EnrollmentKey is only present when the tenant uses the legacy reusable key.
/// </summary>
internal sealed record HealthConfig(Guid TenantId, string ServerUrl, string? EnrollmentKey);

/// <summary>
/// Body POSTed to /api/agent/health/{tenantId}. TenantId travels in the route, so the
/// body carries the machine name, (optionally) the legacy enrollment key, and — collector
/// phase — the stable machine-identity signals (MachineGuid + full DnsHostName). The two
/// identity fields are optional/defaulted so the record stays a superset of the 0.4.45
/// shape; the server stores them and still matches by (tenant, MachineName).
/// </summary>
public sealed record HealthPingPayload(
    string MachineName,
    string? EnrollmentKey,
    string? MachineGuid = null,
    string? DnsHostName = null);

// Source-generated serialization so the trimmed self-contained publish never needs
// reflection-based JSON (trim-safe; no runtime trim warnings).
[JsonSerializable(typeof(HealthPingPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
