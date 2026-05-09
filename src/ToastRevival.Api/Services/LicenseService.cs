using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class LicenseService : ILicenseService
{
    // M6 D1: tier limits.  LicenseCount=0 on the Tenant row means unlimited.
    private static readonly Dictionary<SubscriptionTier, int> TierLimits = new()
    {
        [SubscriptionTier.Free]       = 10,
        [SubscriptionTier.Pro]        = 250,
        [SubscriptionTier.Enterprise] = 0,   // 0 = unlimited
    };

    private readonly AppDbContext _db;

    public LicenseService(AppDbContext db)
    {
        _db = db;
    }

    public string GetTierLabel(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Free       => "Free",
        SubscriptionTier.Pro        => "Pro",
        SubscriptionTier.Enterprise => "Enterprise",
        _                           => tier.ToString(),
    };

    public int GetDeviceLimit(SubscriptionTier tier) =>
        TierLimits.TryGetValue(tier, out var limit) ? limit : 10;

    public async Task<bool> CanRegisterDeviceAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null) return false;

        // Canceled billing = hard block on new registrations
        if (tenant.BillingStatus == BillingStatus.Canceled) return false;

        // Enterprise (LicenseCount=0) is always unlimited
        if (tenant.LicenseCount == 0) return true;

        var activeCount = await _db.Devices.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.Status == DeviceStatus.Active)
            .CountAsync(ct);

        return activeCount < tenant.LicenseCount;
    }

    public async Task IncrementConsumedAsync(Tenant tenant, CancellationToken ct = default)
    {
        tenant.ConsumedCount = Math.Max(0, tenant.ConsumedCount) + 1;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DecrementConsumedAsync(Tenant tenant, CancellationToken ct = default)
    {
        tenant.ConsumedCount = Math.Max(0, tenant.ConsumedCount - 1);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SyncConsumedCountAsync(Tenant tenant, CancellationToken ct = default)
    {
        var count = await _db.Devices.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id && d.Status == DeviceStatus.Active)
            .CountAsync(ct);

        if (tenant.ConsumedCount != count)
        {
            tenant.ConsumedCount = count;
            await _db.SaveChangesAsync(ct);
        }
    }
}
