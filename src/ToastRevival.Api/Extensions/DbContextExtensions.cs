using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Extensions;

/// <summary>
/// ARCH-M2: Shared DB query helpers extracted from per-controller private copies.
/// </summary>
public static class DbContextExtensions
{
    /// <summary>
    /// Returns true when the device JWT must be refused — the device row is missing
    /// or Decommissioned (SES-3), OR the owning tenant is suspended (SES-2).
    /// Mirrors the logic previously copy-pasted in DevicesController, NotificationsController,
    /// and NotificationHub.OnConnectedAsync.
    /// </summary>
    public static async Task<bool> IsDeviceRevokedAsync(this AppDbContext db, Guid deviceId, CancellationToken ct = default)
    {
        var row = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Status, TenantSuspended = d.Tenant.SuspendedAt != null })
            .FirstOrDefaultAsync(ct);
        return row is null
            || row.Status == DeviceStatus.Decommissioned
            || row.TenantSuspended;
    }
}
