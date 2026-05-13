using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class LicenseService : ILicenseService
{
    private readonly AppDbContext _db;
    private readonly bool _requireBilling;

    public LicenseService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _requireBilling = config.GetValue<bool>("TOAST_REQUIRE_BILLING");
    }

    public async Task<bool> CanRegisterDeviceAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Self-hosted deployments set TOAST_REQUIRE_BILLING=false (or leave it unset).
        // No device cap applies — the host owns the infrastructure.
        if (!_requireBilling) return true;

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null) return false;

        // Trial tenants are capped at 2 devices.
        if (tenant.BillingStatus == BillingStatus.Trialing)
            return tenant.ConsumedCount < BillingPlanRules.TrialDeviceLimit;

        // Free tier: devices 1-25 always allowed, no Stripe required.
        if (tenant.ConsumedCount <= BillingPlanRules.FreeTierDeviceLimit)
            return true;

        // Above free tier: a real Stripe subscription must exist and not be canceled.
        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId)) return false;
        if (tenant.BillingStatus == BillingStatus.Canceled)    return false;

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
