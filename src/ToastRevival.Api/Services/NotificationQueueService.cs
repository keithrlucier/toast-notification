using System.Security.Cryptography;
using System.Text;
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

        var (payloadJson, signature) = BuildSignedPayload(notification, tenant.SigningKey);

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

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        // Match the agent's deserialization defaults so the byte sequence we sign
        // is the byte sequence the agent verifies. No camelCase rewrite — properties
        // are already lowerCamel here.
        WriteIndented = false,
    };

    private static (string PayloadJson, string Signature) BuildSignedPayload(Notification n, string signingKey)
    {
        var payload = new
        {
            notificationId = n.Id,
            title = n.Title,
            bodyLine1 = n.BodyLine1,
            bodyLine2 = n.BodyLine2,
            heroImageUrl = n.HeroImageUrl,
            logoUrl = n.LogoUrl,
            actionButtons = n.ActionButtonsJson is not null
                ? JsonSerializer.Deserialize<JsonElement?>(n.ActionButtonsJson)
                : null,
            audioSetting = n.AudioSetting,
            scenario = n.Scenario.ToString().ToLower(),
        };

        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        using var hmac = new HMACSHA256(Convert.FromBase64String(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson)));
        return (payloadJson, signature);
    }
}
