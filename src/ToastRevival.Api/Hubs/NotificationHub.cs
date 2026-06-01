using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

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
                    .FirstOrDefaultAsync(d => d.Id == deviceId.Value);

                if (device is null || device.Status == DeviceStatus.Decommissioned)
                {
                    await Clients.Caller.SendAsync("DeviceDecommissioned");
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
            ConnectedDevices.TryRemove(deviceId.Value, out _);
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
            delivery.Status = action.StartsWith("dismiss") ? DeliveryStatus.Dismissed : DeliveryStatus.Clicked;
            delivery.InteractedAt = DateTime.UtcNow;
            delivery.Action = action;
            await db.SaveChangesAsync();

            await Clients.Group($"dashboard-{tenantId}")
                .SendAsync("DeliveryUpdate", notificationId, deviceId, action);
        }
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
