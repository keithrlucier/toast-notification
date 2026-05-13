using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface IStripeBillingSyncService
{
    Task SyncSubscriptionQuantityAsync(Tenant tenant, CancellationToken ct = default);
}
