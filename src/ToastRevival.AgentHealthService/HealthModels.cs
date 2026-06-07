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
/// body carries only the machine name and (optionally) the legacy enrollment key.
/// </summary>
public sealed record HealthPingPayload(string MachineName, string? EnrollmentKey);

// Source-generated serialization so the trimmed self-contained publish never needs
// reflection-based JSON (trim-safe; no runtime trim warnings).
[JsonSerializable(typeof(HealthPingPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
