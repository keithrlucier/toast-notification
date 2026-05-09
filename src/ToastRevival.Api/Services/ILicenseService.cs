using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface ILicenseService
{
    /// <summary>
    /// True when the tenant can register another device.
    /// Per-device billing has no license ceiling; canceled billing blocks new registrations.
    /// </summary>
    Task<bool> CanRegisterDeviceAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Increment ConsumedCount and save.</summary>
    Task IncrementConsumedAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Decrement ConsumedCount (floor 0) and save.</summary>
    Task DecrementConsumedAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Recompute ConsumedCount from active device rows.</summary>
    Task SyncConsumedCountAsync(Tenant tenant, CancellationToken ct = default);
}
