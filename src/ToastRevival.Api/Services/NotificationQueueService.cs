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
    /// Time a Notification is allowed to sit in the Sending state before the
    /// startup recovery sweep considers it orphaned. Five minutes is long
    /// enough to swallow a normal restart with an in-flight fanout, short
    /// enough that a stuck row doesn't shadow real product behavior for an hour.
    /// </summary>
    private static readonly TimeSpan OrphanThreshold = TimeSpan.FromMinutes(5);

    // PERF-M2: bounded channel provides backpressure — callers receive false from Enqueue()
    // when full (capacity 10,000) and can return 503 rather than silently queueing unbounded.
    // FullMode.Wait causes WriteAsync to back-pressure; TryWrite returns false when full,
    // which is what Enqueue() surfaces to callers.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(10_000)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
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

    // Manual queue-depth tracking. Channel.CreateUnbounded with
    // SingleReader=true uses an internal queue type whose Reader.CanCount
    // returns false — the single-consumer optimization deliberately skips
    // Count to avoid the synchronization needed to keep it consistent.
    // We track a mirror counter via Interlocked so the health endpoint can
    // report depth without losing the perf win. Producer increments after
    // a successful TryWrite; consumer decrements after a successful read.
    private int _queueDepth;

    // PERF-M2: returns false when channel is full so callers can respond 503.
    public bool Enqueue(Guid notificationId)
    {
        if (_channel.Writer.TryWrite(notificationId))
        {
            Interlocked.Increment(ref _queueDepth);
            return true;
        }
        return false;
    }

    public int QueueDepth => Volatile.Read(ref _queueDepth);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Recover orphan Sending rows from a previous process that crashed
        // mid-fanout. Run once before entering the channel loop.
        try
        {
            await RecoverOrphansAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orphan recovery sweep failed at startup");
        }

        // Re-enqueue scheduled notifications that became due while the
        // service was offline (startup backfill).
        await EnqueueDueScheduledAsync(ct);

        // Run the scheduler loop and the queue consumer concurrently until cancellation.
        await Task.WhenAll(RunSchedulerLoopAsync(ct), ProcessQueueAsync(ct));
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var notificationId in _channel.Reader.ReadAllAsync(ct))
        {
            // Decrement before ProcessAsync — depth measures "items waiting
            // in the channel," not "items in flight." A long-running process
            // step doesn't pin depth high; that's separate signal (could add
            // an in-flight gauge later if useful).
            Interlocked.Decrement(ref _queueDepth);
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
    /// Polls every 60 seconds for Queued notifications whose ScheduledAt has
    /// arrived and enqueues them. Runs alongside the channel consumer.
    /// </summary>
    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await EnqueueDueScheduledAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Finds all Queued notifications with a past ScheduledAt and enqueues them.
    /// Called at startup (backfill) and every 60 seconds thereafter.
    /// </summary>
    private async Task EnqueueDueScheduledAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // REL-002-R: include immediate notifications (ScheduledAt == null) in startup
            // backfill. Previously these were excluded, leaving any notification committed
            // to the DB but not enqueued (e.g. process crash between SaveChanges and
            // Enqueue) stranded forever with no recovery path.
            var due = await db.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.Status == NotificationStatus.Queued
                         && (n.ScheduledAt == null || n.ScheduledAt <= DateTime.UtcNow))
                .Select(n => n.Id)
                .ToListAsync(ct);

            foreach (var id in due)
                Enqueue(id);

            if (due.Count > 0)
                _logger.LogInformation("Enqueued {Count} due scheduled notification(s)", due.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled notification sweep failed");
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

        var cutoff = DateTime.UtcNow - OrphanThreshold;

        // PERF-M3: single bulk UPDATE instead of ToListAsync + foreach to avoid
        // materializing full entities just to set two columns.
        var count = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.Status == NotificationStatus.Sending && n.SentAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Failed)
                .SetProperty(n => n.CompletedAt, DateTime.UtcNow),
                ct);

        if (count > 0)
            _logger.LogWarning(
                "Recovered {Count} orphan Sending notification(s) older than {ThresholdMinutes}m to Failed; pending deliveries left intact for catch-up",
                count, OrphanThreshold.TotalMinutes);
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

        // Guard against duplicate-enqueue from startup backfill + timer tick overlap.
        if (notification.Status != NotificationStatus.Queued)
        {
            _logger.LogDebug("Skipping notification {NotificationId} — already in {Status} state",
                notificationId, notification.Status);
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
            // REL-L2: retry up to 3 attempts with exponential back-off before giving up.
            // On final failure the delivery is left as Pending (not Failed permanently)
            // so the catch-up polling mechanism can serve it when the agent reconnects.
            bool delivered = false;
            Exception? lastEx = null;
            for (int attempt = 0; attempt < 3; attempt++)
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
                    delivered = true;
                    break;
                }
                catch (Exception ex) when (attempt < 2)
                {
                    lastEx = ex;
                    _logger.LogDebug(ex, "Transient push failure to device {DeviceId} (attempt {Attempt}/3)", delivery.DeviceId, attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (delivered)
            {
                sent++;
            }
            else
            {
                // Leave as Pending so catch-up polling can retry on agent reconnect.
                _logger.LogWarning(lastEx, "Failed to push to device {DeviceId} after 3 attempts — leaving Pending for catch-up", delivery.DeviceId);
                delivery.ErrorMessage = lastEx?.Message;
                // Status stays at its current value (not marking Failed) to enable catch-up.
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
