using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class LicenseService : ILicenseService
{
    private readonly AppDbContext _db;

    public LicenseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanRegisterDeviceAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null) return false;

        // Canceled billing = hard block on new registrations
        if (tenant.BillingStatus == BillingStatus.Canceled) return false;

        return true;
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
