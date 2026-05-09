using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize(Policy = "PlatformAdmin")]
[EnableRateLimiting("tenant-per-minute")]
public class SystemController : ControllerBase
{
    private readonly AppDbContext _db;

    public SystemController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> Tenants()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Subdomain,
                t.BillingStatus,
                t.LicenseStart,
                t.LicenseEnd,
                t.CreatedAt,
            })
            .ToListAsync();

        var deviceCounts = await ActiveDeviceCountsAsync();
        var userCounts = await _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        return Ok(new
        {
            tenants = tenants.Select(t =>
            {
                var deviceCount = deviceCounts.GetValueOrDefault(t.Id);
                return new
                {
                    t.Id,
                    t.Name,
                    t.Subdomain,
                    deviceCount,
                    userCount = userCounts.GetValueOrDefault(t.Id),
                    billingStatus = t.BillingStatus.ToString(),
                    subscriptionStartedAt = t.LicenseStart,
                    subscriptionEndsAt = t.LicenseEnd,
                    monthlyBill = BillingPlanRules.CurrentBill(deviceCount),
                    t.CreatedAt,
                };
            }),
        });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> Tenant(Guid id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        var userRows = await _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.TenantId == id)
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Role,
                u.IsPlatformAdmin,
                mfaEnabled = u.MfaSecret != null,
                u.LastLogin,
                u.CreatedAt,
            })
            .ToListAsync();
        var users = userRows.Select(u => new
        {
            u.Id,
            u.Email,
            role = u.Role.ToString(),
            u.IsPlatformAdmin,
            u.mfaEnabled,
            u.LastLogin,
            u.CreatedAt,
        });

        var deviceStatusCounts = await _db.Devices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.TenantId == id)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var since = DateTime.UtcNow.AddDays(-30);
        var recentNotificationVolume = await _db.Notifications.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.TenantId == id && n.CreatedAt >= since)
            .CountAsync();

        var activeDeviceCount = deviceStatusCounts
            .Where(x => x.Status == DeviceStatus.Active)
            .Sum(x => x.Count);

        return Ok(new
        {
            tenant = new
            {
                tenant.Id,
                tenant.Name,
                tenant.Subdomain,
                billingStatus = tenant.BillingStatus.ToString(),
                tenant.LicenseStart,
                tenant.LicenseEnd,
                tenant.StripeCustomerId,
                tenant.StripeSubscriptionId,
                activeDeviceCount,
                monthlyBill = BillingPlanRules.CurrentBill(activeDeviceCount),
                recentNotificationVolume,
                tenant.CreatedAt,
                tenant.UpdatedAt,
            },
            users,
            deviceStatusCounts = deviceStatusCounts.Select(x => new { status = x.Status.ToString(), x.Count }),
        });
    }

    [HttpGet("billing-overview")]
    public async Task<IActionResult> BillingOverview()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .Select(t => new { t.Id, t.BillingStatus })
            .ToListAsync();

        var deviceCounts = await ActiveDeviceCountsAsync();
        var totalDevices = deviceCounts.Values.Sum();
        var monthlyRecurringRevenue = tenants
            .Where(t => t.BillingStatus != BillingStatus.Canceled)
            .Sum(t => BillingPlanRules.CurrentBill(deviceCounts.GetValueOrDefault(t.Id)));

        var byBillingStatus = tenants
            .GroupBy(t => t.BillingStatus)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .OrderBy(x => x.status)
            .ToList();

        return Ok(new
        {
            totalTenants = tenants.Count,
            totalDevices,
            monthlyRecurringRevenue,
            byBillingStatus,
        });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> Devices([FromQuery] Guid? tenantId = null)
    {
        var query = _db.Devices.IgnoreQueryFilters()
            .Include(d => d.Tenant)
            .AsNoTracking()
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(d => d.TenantId == tenantId.Value);

        var deviceRows = await query
            .OrderByDescending(d => d.LastPing ?? d.RegisteredAt)
            .Take(500)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                tenantName = d.Tenant.Name,
                d.DeviceName,
                d.Username,
                d.OsVersion,
                d.AgentVersion,
                d.Status,
                d.LastPing,
                d.RegisteredAt,
            })
            .ToListAsync();
        var devices = deviceRows.Select(d => new
        {
            d.Id,
            d.TenantId,
            d.tenantName,
            d.DeviceName,
            d.Username,
            d.OsVersion,
            d.AgentVersion,
            status = d.Status.ToString(),
            d.LastPing,
            d.RegisteredAt,
        });

        return Ok(new { devices });
    }

    private async Task<Dictionary<Guid, int>> ActiveDeviceCountsAsync()
    {
        return await _db.Devices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Status == DeviceStatus.Active)
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);
    }
}
