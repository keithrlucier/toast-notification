using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.Hubs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class NotificationQueueService : BackgroundService, INotificationQueueService
{
    /// <summary>
    /// Time a Notification is allowed to sit in the Sending state before it is
    /// considered orphaned by the M2.B startup recovery sweep. Five minutes is
    /// long enough to swallow a normal restart with an in-flight fanout, short
    /// enough that a stuck row doesn't shadow real product behavior for an hour.
    /// </summary>
    private static readonly TimeSpan OrphanThreshold = TimeSpan.FromMinutes(5);

    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationQueueService> _logger;

    public NotificationQueueService(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotificationHub> hub,
        ILogger<NotificationQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    public void Enqueue(Guid notificationId) =>
        _channel.Writer.TryWrite(notificationId);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // M2.B: recover orphan Sending rows from a previous process that crashed
        // mid-fanout (INFO-M2A-003). Run once before entering the channel loop.
        // Notification → Failed; deliveries STAY Pending so the catch-up endpoint
        // can still deliver them to agents on reconnect (Carl's M2.B overrule on
        // the original "deliveries to Failed accordingly" plan, which would have
        // defeated catch-up).
        try
        {
            await RecoverOrphansAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orphan recovery sweep failed at startup");
            // Non-fatal — the queue can still serve new traffic without recovery.
        }

        await foreach (var notificationId in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessAsync(notificationId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notification {NotificationId}", notificationId);
            }
        }
    }

    /// <summary>
    /// Sweep Notifications stuck in Sending past the orphan threshold to Failed.
    /// Pending deliveries are NOT touched — the catch-up endpoint serves them on
    /// agent reconnect. Idempotent (rerunning after a fast restart finds nothing
    /// because the threshold rejects rows under 5 minutes old).
    /// </summary>
    private async Task RecoverOrphansAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var threshold = DateTime.UtcNow - OrphanThreshold;
        var orphans = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.Status == NotificationStatus.Sending && n.SentAt < threshold)
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var n in orphans)
        {
            n.Status = NotificationStatus.Failed;
            n.CompletedAt = now;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Recovered {Count} orphan Sending notification(s) older than {ThresholdMinutes}m to Failed; pending deliveries left intact for catch-up",
            orphans.Count, OrphanThreshold.TotalMinutes);
    }

    private async Task ProcessAsync(Guid notificationId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Load without tenant filter — queue service operates outside request context
        var notification = await db.Notifications
            .IgnoreQueryFilters()
            .Include(n => n.Deliveries)
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct);

        if (notification is null)
        {
            _logger.LogWarning("Notification {NotificationId} not found", notificationId);
            return;
        }

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == notification.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogError("Tenant {TenantId} missing for notification {NotificationId}",
                notification.TenantId, notificationId);
            return;
        }

        notification.Status = NotificationStatus.Sending;
        notification.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (payloadJson, signature) = NotificationPayloadBuilder.BuildSigned(notification, tenant.SigningKey);

        int sent = 0;
        foreach (var delivery in notification.Deliveries)
        {
            try
            {
                // Send (payloadJson, signature) as separate args. Pre-serialized JSON
                // ensures the agent verifies the exact byte sequence we signed; otherwise
                // SignalR's transport-side serializer could produce a different encoding
                // than the one we HMAC'd over.
                await _hub.Clients
                    .Group($"device-{delivery.DeviceId}")
                    .SendAsync("ReceiveNotification", payloadJson, signature, ct);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push to device {DeviceId}", delivery.DeviceId);
                delivery.Status = DeliveryStatus.Failed;
                delivery.ErrorMessage = ex.Message;
            }
        }

        notification.Status = sent == notification.Deliveries.Count
            ? NotificationStatus.Sent
            : sent == 0
                ? NotificationStatus.Failed
                : NotificationStatus.PartialFailure;
        notification.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Notification {NotificationId} sent to {Sent}/{Total} devices",
            notificationId, sent, notification.Deliveries.Count);
    }
}
