using System.Text.Json;
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

        notification.Status = NotificationStatus.Sending;
        notification.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var payload = BuildPayload(notification);

        int sent = 0;
        foreach (var delivery in notification.Deliveries)
        {
            try
            {
                await _hub.Clients
                    .Group($"device-{delivery.DeviceId}")
                    .SendAsync("ReceiveNotification", payload, ct);
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

    private static object BuildPayload(Notification n) => new
    {
        notificationId = n.Id,
        title = n.Title,
        bodyLine1 = n.BodyLine1,
        bodyLine2 = n.BodyLine2,
        heroImageUrl = n.HeroImageUrl,
        logoUrl = n.LogoUrl,
        actionButtons = n.ActionButtonsJson is not null
            ? JsonSerializer.Deserialize<object>(n.ActionButtonsJson)
            : null,
        audioSetting = n.AudioSetting,
        scenario = n.Scenario.ToString().ToLower(),
    };
}
