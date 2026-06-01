using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class AnalyticsController : ControllerBase, IActionFilter
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db) => _db = db;

    // Token-type confinement: tenant analytics is a dashboard surface. A device
    // JWT (type="device") satisfies the controller's [Authorize] but must never
    // read tenant-wide analytics. Reject device tokens before any action runs.
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (User.FindFirstValue("type") == "device")
            context.Result = new ForbidResult();
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var sentCount = await _db.Notifications
            .Where(n => n.SentAt >= since)
            .CountAsync();

        // Materialize statuses; avoids translating enum.ToString() server-side
        var statuses = await _db.NotificationDeliveries
            .Where(d => d.CreatedAt >= since)
            .Select(d => d.Status)
            .ToListAsync();

        var total     = statuses.Count;
        var delivered = statuses.Count(s => s == DeliveryStatus.Delivered || s == DeliveryStatus.Clicked || s == DeliveryStatus.Dismissed);
        var clicked   = statuses.Count(s => s == DeliveryStatus.Clicked);

        var deliveryRate     = total     > 0 ? Math.Round((double)delivered / total     * 100, 1) : 0.0;
        var interactionRate  = delivered > 0 ? Math.Round((double)clicked   / delivered * 100, 1) : 0.0;

        var activeDeviceCount = await _db.Devices
            .Where(d => d.LastPing >= DateTime.UtcNow.AddHours(-24) && d.Status == DeviceStatus.Active)
            .CountAsync();

        return Ok(new { sentCount, deliveryRate, interactionRate, activeDeviceCount });
    }

    [HttpGet("volume")]
    public async Task<IActionResult> Volume([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var sentByDay = await _db.Notifications
            .Where(n => n.SentAt >= since && n.SentAt != null)
            .GroupBy(n => n.SentAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var deliveredByDay = await _db.NotificationDeliveries
            .Where(d => d.DeliveredAt >= since && d.DeliveredAt != null
                && (d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.Clicked))
            .GroupBy(d => d.DeliveredAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var sentMap  = sentByDay.ToDictionary(x => x.Date, x => x.Count);
        var delivMap = deliveredByDay.ToDictionary(x => x.Date, x => x.Count);

        var result = Enumerable.Range(0, days)
            .Select(i => since.AddDays(i))
            .Select(date => new
            {
                date      = date.ToString("MMM d"),
                sent      = sentMap.GetValueOrDefault(date, 0),
                delivered = delivMap.GetValueOrDefault(date, 0),
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("breakdown")]
    public async Task<IActionResult> Breakdown([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var rawStatuses = await _db.NotificationDeliveries
            .Where(d => d.CreatedAt >= since)
            .Select(d => d.Status)
            .ToListAsync();

        var byStatus = rawStatuses
            .GroupBy(s => s)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Join notifications with their template categories for the period.
        // Both sides carry global tenant query filters so no explicit TenantId filter needed.
        var rawCategories = await _db.Notifications
            .Where(n => n.SentAt >= since && n.TemplateId != null)
            .Join(
                _db.NotificationTemplates,
                n => n.TemplateId,
                t => (Guid?)t.Id,
                (n, t) => t.Category)
            .ToListAsync();

        var byTemplate = rawCategories
            .GroupBy(c => c)
            .ToDictionary(
                g => g.Key.ToString().ToLowerInvariant(),
                g => g.Count());

        return Ok(new { byStatus, byTemplate });
    }
}
