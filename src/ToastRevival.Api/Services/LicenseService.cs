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

        return IsWithinCap(tenant);
    }

    public async Task<bool> TryRegisterDeviceAtomicAsync(
        Tenant tenant, Device device, CancellationToken ct = default)
    {
        // Previously the controller called CanRegisterDeviceAsync, then issued
        // a separate INSERT — two concurrent /devices/register calls for the
        // same trial tenant could both pass the 2-device gate before either
        // row committed, exceeding the cap. Serialize per-tenant at a
        // transaction-scoped PostgreSQL advisory lock so the check and the
        // insert are one critical section. The lock auto-releases on commit
        // OR rollback; different tenants hash to different keys and proceed
        // in parallel.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})",
            new object[] { (long)tenant.Id.GetHashCode() },
            ct);

        // The tenant entity was fetched before the lock — its ConsumedCount may be
        // stale if a sibling registration just committed. Re-read inside the lock
        // so the cap check sees authoritative state.
        await _db.Entry(tenant).ReloadAsync(ct);

        if (!IsWithinCap(tenant))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        _db.Devices.Add(device);
        tenant.ConsumedCount = Math.Max(0, tenant.ConsumedCount) + 1;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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

    private bool IsWithinCap(Tenant tenant)
    {
        // Suspended tenants can't register new devices regardless of billing mode.
        if (tenant.SuspendedAt.HasValue) return false;

        if (!_requireBilling) return true;

        // Complimentary tenants (Platform Admin grant) bypass all caps —
        // no trial limit, no free-tier ceiling, no Stripe requirement.
        if (tenant.IsComplimentary) return true;

        // Trial tenants are capped at 2 devices.
        if (tenant.BillingStatus == BillingStatus.Trialing)
            return tenant.ConsumedCount < BillingPlanRules.TrialDeviceLimit;

        // Free tier: devices 1-25 always allowed, no Stripe required.
        // ConsumedCount is the count BEFORE this registration's increment, so it
        // is the index of the seat about to be taken. While < 25 devices are
        // already registered, the next seat (1..25) is free. At ConsumedCount==25
        // the next seat is device 26 — the first BILLABLE seat (BillableDevices =
        // Max(0, count-25)) — so it must fall through to the subscription check.
        // Using `<` (not `<=`) closes the off-by-one that admitted device 26 free.
        if (tenant.ConsumedCount < BillingPlanRules.FreeTierDeviceLimit)
            return true;

        // Above free tier: a real Stripe subscription must exist and not be canceled.
        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId)) return false;
        if (tenant.BillingStatus == BillingStatus.Canceled)    return false;

        return true;
    }
}
