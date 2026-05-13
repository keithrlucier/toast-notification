using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface ILicenseService
{
    /// <summary>
    /// True when the tenant can register another device.
    /// Per-device billing has no license ceiling; canceled billing blocks new registrations.
    /// </summary>
    Task<bool> CanRegisterDeviceAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Atomically validates the per-tenant cap, inserts the device row, and
    /// increments ConsumedCount under a per-tenant PostgreSQL advisory lock.
    /// Concurrent callers for the same tenant serialize at the lock; different
    /// tenants proceed in parallel. Closes INFO-M11-SW-001 — the TOCTOU race
    /// between <see cref="CanRegisterDeviceAsync"/> and the device INSERT that
    /// previously let two concurrent trial registrations both pass the 2-device
    /// gate. Returns true when the device was committed; false when the cap is
    /// hit or billing is canceled (no rows written).
    /// </summary>
    Task<bool> TryRegisterDeviceAtomicAsync(Tenant tenant, Device device, CancellationToken ct = default);

    /// <summary>Increment ConsumedCount and save.</summary>
    Task IncrementConsumedAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Decrement ConsumedCount (floor 0) and save.</summary>
    Task DecrementConsumedAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Recompute ConsumedCount from active device rows.</summary>
    Task SyncConsumedCountAsync(Tenant tenant, CancellationToken ct = default);
}
