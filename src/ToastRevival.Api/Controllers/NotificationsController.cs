using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
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

    public NotificationsController(AppDbContext db, INotificationQueueService queue, IAuditService audit)
    {
        _db = db;
        _queue = queue;
        _audit = audit;
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
