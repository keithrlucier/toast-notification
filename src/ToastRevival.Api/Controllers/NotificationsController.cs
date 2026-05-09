using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Hubs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationQueueService _queue;
    private readonly IAuditService _audit;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationsController(
        AppDbContext db,
        INotificationQueueService queue,
        IAuditService audit,
        IHubContext<NotificationHub> hub)
    {
        _db = db;
        _queue = queue;
        _audit = audit;
        _hub = hub;
    }

    [HttpPost]
    public async Task<ActionResult<NotificationResponse>> Send([FromBody] SendNotificationRequest req)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var senderId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Expand targets to device IDs
        var deviceIds = await ResolveTargetDeviceIds(req, tenantId);
        if (deviceIds.Count == 0 && req.TargetType != TargetType.All)
            return BadRequest("No devices matched the specified targets.");

        var notification = new Notification
        {
            TenantId = tenantId,
            TemplateId = req.TemplateId,
            SenderId = senderId,
            Title = req.Title,
            BodyLine1 = req.BodyLine1,
            BodyLine2 = req.BodyLine2,
            HeroImageUrl = req.HeroImageUrl,
            LogoUrl = req.LogoUrl,
            ActionButtonsJson = req.ActionButtons is not null
                ? JsonSerializer.Serialize(req.ActionButtons) : null,
            AudioSetting = req.AudioSetting,
            Scenario = req.Scenario,
            TargetType = req.TargetType,
            TargetIdsJson = req.TargetIds is not null
                ? JsonSerializer.Serialize(req.TargetIds) : null,
            TargetDeviceCount = deviceIds.Count,
            ScheduledAt = req.ScheduledAt,
        };

        _db.Notifications.Add(notification);

        foreach (var deviceId in deviceIds)
        {
            _db.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = notification.Id,
                DeviceId = deviceId,
                TenantId = tenantId,
            });
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, senderId, "notification.send", "Notification",
            notification.Id.ToString(),
            new { req.Title, req.TargetType, deviceCount = deviceIds.Count },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        // Schedule immediately unless a future scheduledAt was requested
        if (req.ScheduledAt is null || req.ScheduledAt <= DateTime.UtcNow)
            _queue.Enqueue(notification.Id);

        return Accepted(new NotificationResponse(
            notification.Id, notification.Title, notification.BodyLine1,
            notification.BodyLine2, notification.Status.ToString(),
            notification.TargetType, notification.TargetDeviceCount,
            notification.ScheduledAt, notification.SentAt, notification.CreatedAt));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationHistoryItem>>> History()
    {
        var items = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(n => new NotificationHistoryItem(
                n.Id, n.Title, n.Status.ToString(), n.TargetDeviceCount,
                n.Deliveries.Count(d => d.Status == DeliveryStatus.Delivered),
                n.Deliveries.Count(d => d.Status == DeliveryStatus.Clicked),
                n.CreatedAt, n.SentAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NotificationResponse>> Get(Guid id)
    {
        var n = await _db.Notifications.FindAsync(id);
        if (n is null) return NotFound();

        return Ok(new NotificationResponse(
            n.Id, n.Title, n.BodyLine1, n.BodyLine2,
            n.Status.ToString(), n.TargetType, n.TargetDeviceCount,
            n.ScheduledAt, n.SentAt, n.CreatedAt));
    }

    /// <summary>
    /// Device-authenticated REST fallback for reporting an interaction. The hub
    /// path is preferred (NotificationHub.ReportInteraction) — this endpoint exists
    /// for the MSIX activation handler exit path, where the framework launches a
    /// short-lived agent process to deliver a button-click event when no primary
    /// agent instance is running and standing up a SignalR connection just to
    /// post one event would be wasteful.
    /// </summary>
    [HttpPost("{id:guid}/interactions")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<IActionResult> ReportInteraction(Guid id, [FromBody] InteractionRequest req)
    {
        var typeClaim = User.FindFirstValue("type");
        var deviceIdClaim = User.FindFirstValue("deviceId");
        var tenantIdClaim = User.FindFirstValue("tenantId");

        if (typeClaim != "device"
            || !Guid.TryParse(deviceIdClaim, out var deviceId)
            || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized();
        }

        var delivery = await _db.NotificationDeliveries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.NotificationId == id && d.DeviceId == deviceId);

        if (delivery is null || delivery.TenantId != tenantId) return NotFound();

        delivery.Status = req.Action.StartsWith("dismiss")
            ? DeliveryStatus.Dismissed
            : DeliveryStatus.Clicked;
        delivery.InteractedAt = DateTime.UtcNow;
        delivery.Action = req.Action;
        await _db.SaveChangesAsync();

        // Push the same DeliveryUpdate that ReportInteraction() does so dashboard
        // users see a consistent stream regardless of which path delivered the event.
        await _hub.Clients.Group($"tenant-{tenantId}")
            .SendAsync("DeliveryUpdate", id, deviceId, req.Action);

        return NoContent();
    }

    private async Task<List<Guid>> ResolveTargetDeviceIds(SendNotificationRequest req, Guid tenantId)
    {
        IQueryable<Device> query = _db.Devices.Where(d => d.Status == DeviceStatus.Active);

        return req.TargetType switch
        {
            TargetType.All => await query.Select(d => d.Id).ToListAsync(),

            TargetType.Device when req.TargetIds?.Count > 0 =>
                await query.Where(d => req.TargetIds.Contains(d.Id))
                           .Select(d => d.Id).ToListAsync(),

            TargetType.Group when req.TargetIds?.Count > 0 =>
                await _db.DeviceGroupMembers
                    .Where(m => req.TargetIds.Contains(m.DeviceGroupId))
                    .Select(m => m.DeviceId).Distinct().ToListAsync(),

            _ => [],
        };
    }
}
