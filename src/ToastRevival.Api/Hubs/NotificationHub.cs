using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    // Tracks online devices for delivery status reporting
    public static readonly ConcurrentDictionary<Guid, string> ConnectedDevices = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(IServiceScopeFactory scopeFactory, ILogger<NotificationHub> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var tenantId = GetTenantId();
        var type = Context.User?.FindFirstValue("type");

        // tenant-{id} carries agent-facing pushes (e.g. AppearanceUpdated) that
        // BOTH devices and dashboard users may consume, so every connection joins
        // it. Dashboard-only recon events (DeviceConnected/DeviceDisconnected/
        // DeliveryUpdate) go to dashboard-{id}, which ONLY non-device (user)
        // connections join — a device must not be able to harvest other devices'
        // online/offline transitions or in-flight notification IDs.
        if (tenantId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
            if (type != "device")
                await Groups.AddToGroupAsync(Context.ConnectionId, $"dashboard-{tenantId}");
        }

        if (type == "device")
        {
            var deviceId = GetDeviceId();
            if (deviceId.HasValue)
            {
                // Reject decommissioned devices — their JWT is still cryptographically valid but
                // the device has been removed from the tenant. Tell the agent so it can clear its
                // local config and re-register as a new device on next launch.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var device = await db.Devices.IgnoreQueryFilters()
                    .Include(d => d.Tenant)
                    .FirstOrDefaultAsync(d => d.Id == deviceId.Value);

                if (device is null || device.Status == DeviceStatus.Decommissioned)
                {
                    await Clients.Caller.SendAsync("DeviceDecommissioned");
                    Context.Abort();
                    return;
                }

                // FIX-SES-2 (2026-06-01): a suspended tenant is the operator kill switch.
                // Drop its agents' hub connections so they stop receiving AppearanceUpdated
                // pushes (and the device-{id} fanout) on their 365-day tokens. This mirrors
                // the device-JWT REST guards (IsDeviceRevoked). NOT a decommission — we send
                // no "DeviceDecommissioned" so the agent keeps its config and simply
                // reconnects if/when the tenant is resumed. (?. guards an orphaned tenant FK.)
                if (device.Tenant?.SuspendedAt != null)
                {
                    Context.Abort();
                    return;
                }

                ConnectedDevices[deviceId.Value] = Context.ConnectionId;
                await Groups.AddToGroupAsync(Context.ConnectionId, $"device-{deviceId}");

                // Notify dashboard users in the tenant that a device came online
                if (tenantId.HasValue)
                    await Clients.Group($"dashboard-{tenantId}").SendAsync("DeviceConnected", deviceId);

                await UpdateLastPingAsync(deviceId.Value);
                _logger.LogInformation("Device {DeviceId} connected (tenant {TenantId})", deviceId, tenantId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = GetDeviceId();
        var tenantId = GetTenantId();

        if (deviceId.HasValue)
        {
            // Core-L1: only remove the mapping if THIS connection is still the one on record.
            // In a reconnect race a stale OnDisconnectedAsync must not delete a
            // deviceId -> connectionId entry that a newer connection just re-added — the
            // two-arg TryRemove is an atomic compare-and-remove on the (key, value) pair.
            // (Distinct from the blessed "dict clears on API restart" behaviour, which is fine.)
            ConnectedDevices.TryRemove(new KeyValuePair<Guid, string>(deviceId.Value, Context.ConnectionId));
            if (tenantId.HasValue)
                await Clients.Group($"dashboard-{tenantId}").SendAsync("DeviceDisconnected", deviceId);
            _logger.LogInformation("Device {DeviceId} disconnected", deviceId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Called by device to confirm it rendered a notification
    public async Task ReportDelivery(Guid notificationId)
    {
        var deviceId = GetDeviceId();
        var tenantId = GetTenantId();
        if (deviceId is null || tenantId is null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var delivery = await db.NotificationDeliveries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.NotificationId == notificationId && d.DeviceId == deviceId);

        if (delivery is not null && delivery.TenantId == tenantId.Value)
        {
            delivery.Status = DeliveryStatus.Delivered;
            delivery.DeliveredAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await Clients.Group($"dashboard-{tenantId}")
                .SendAsync("DeliveryUpdate", notificationId, deviceId, "delivered");
        }
    }

    // Called by device when user interacts with a notification button
    public async Task ReportInteraction(Guid notificationId, string action)
    {
        var deviceId = GetDeviceId();
        var tenantId = GetTenantId();
        if (deviceId is null || tenantId is null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var delivery = await db.NotificationDeliveries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.NotificationId == notificationId && d.DeviceId == deviceId);

        if (delivery is not null && delivery.TenantId == tenantId.Value)
        {
            // Core-L2: guard a null/empty action — a device can invoke this hub method with a
            // null action, and StartsWith would throw NullReferenceException before the delivery
            // is recorded. Missing/blank counts as a non-dismiss interaction (Clicked).
            delivery.Status = !string.IsNullOrEmpty(action) && action.StartsWith("dismiss")
                ? DeliveryStatus.Dismissed
                : DeliveryStatus.Clicked;
            delivery.InteractedAt = DateTime.UtcNow;
            delivery.Action = action;
            await db.SaveChangesAsync();

            await Clients.Group($"dashboard-{tenantId}")
                .SendAsync("DeliveryUpdate", notificationId, deviceId, action);
        }
    }

    // Called by a device after it has fired its own MSI uninstall (CR-P0-006
    // follow-on / DEP-002). Finalizes decommission and decrements license + billing
    // so the dashboard reflects real removal, not just "uninstall requested". The
    // device identity comes from the connection's deviceId claim, so a device can
    // only acknowledge its OWN uninstall -- there is no peer-spoofing surface.
    public async Task UninstallAck()
    {
        var deviceId = GetDeviceId();
        var tenantId = GetTenantId();
        if (deviceId is null || tenantId is null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Atomic compare-and-set: only the caller that actually flips
        // PendingUninstall -> Decommissioned (rows == 1) goes on to decrement. A
        // replay, a second concurrent ack, or a non-pending/foreign device all get
        // rows == 0 and no-op -- so the agent re-acking on every reconnect can never
        // double-decrement this device's seat, and a live device can't self-decommission.
        // (A concurrent admin ConfirmDecommission on the same device is the one remaining
        // double-count window -- pre-existing on that HTTP path; SyncConsumedCountAsync
        // self-heals it and a tenant-wide concurrency token is the real fix.)
        var rows = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.Id == deviceId.Value
                     && d.TenantId == tenantId.Value
                     && d.Status == DeviceStatus.PendingUninstall)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeviceStatus.Decommissioned));
        if (rows == 0) return;

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        if (tenant is not null)
        {
            await scope.ServiceProvider.GetRequiredService<ILicenseService>().DecrementConsumedAsync(tenant);
            await scope.ServiceProvider.GetRequiredService<IStripeBillingSyncService>().SyncSubscriptionQuantityAsync(tenant);
        }

        // Device-initiated finalization: no user principal, so userId is null.
        await scope.ServiceProvider.GetRequiredService<IAuditService>()
            .LogAsync(tenantId.Value, null, "device.uninstall-acked", "Device", deviceId.Value.ToString());

        await Clients.Group($"dashboard-{tenantId}")
            .SendAsync("DeviceDisconnected", deviceId);
    }

    private Guid? GetTenantId()
    {
        var claim = Context.User?.FindFirstValue("tenantId");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetDeviceId()
    {
        var claim = Context.User?.FindFirstValue("deviceId");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private async Task UpdateLastPingAsync(Guid deviceId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var device = await db.Devices.IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == deviceId);
            if (device is not null)
            {
                device.LastPing = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update LastPing for device {DeviceId}", deviceId);
        }
    }
}
