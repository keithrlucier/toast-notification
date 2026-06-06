using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Extensions;
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

        // MT-H3: Explicit TenantId predicate as defense-in-depth alongside EF global filter.
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        var sentCount = await _db.Notifications
            .Where(n => n.TenantId == tenantId && n.SentAt >= since)
            .CountAsync();

        // PERF-H1: Server-side GROUP BY instead of materializing all rows into memory.
        var statusCounts = await _db.NotificationDeliveries
            .Where(d => d.TenantId == tenantId && d.CreatedAt >= since)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var total     = statusCounts.Sum(x => x.Count);
        var delivered = statusCounts
            .Where(x => x.Status == DeliveryStatus.Delivered
                     || x.Status == DeliveryStatus.Clicked
                     || x.Status == DeliveryStatus.Dismissed)
            .Sum(x => x.Count);
        var clicked   = statusCounts
            .Where(x => x.Status == DeliveryStatus.Clicked)
            .Sum(x => x.Count);

        var deliveryRate     = total     > 0 ? Math.Round((double)delivered / total     * 100, 1) : 0.0;
        var interactionRate  = delivered > 0 ? Math.Round((double)clicked   / delivered * 100, 1) : 0.0;

        var activeDeviceCount = await _db.Devices
            .Where(d => d.TenantId == tenantId && d.LastPing >= DateTime.UtcNow.AddHours(-24) && d.Status == DeviceStatus.Active)
            .CountAsync();

        return Ok(new { sentCount, deliveryRate, interactionRate, activeDeviceCount });
    }

    [HttpGet("volume")]
    public async Task<IActionResult> Volume([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-days);

        // MT-H3: Explicit TenantId predicate as defense-in-depth alongside EF global filter.
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        var sentByDay = await _db.Notifications
            .Where(n => n.TenantId == tenantId && n.SentAt >= since && n.SentAt != null)
            .GroupBy(n => n.SentAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var deliveredByDay = await _db.NotificationDeliveries
            .Where(d => d.TenantId == tenantId && d.DeliveredAt >= since && d.DeliveredAt != null
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

        // MT-H3: Explicit TenantId predicates added as defense-in-depth per MT-H3 remediation.
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        // PERF-H1: Server-side GROUP BY instead of materializing all rows into memory.
        var statusCounts = await _db.NotificationDeliveries
            .Where(d => d.TenantId == tenantId && d.CreatedAt >= since)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var byStatus = statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count);

        // MT-H3: Explicit TenantId predicates added as defense-in-depth per MT-H3 remediation.
        var rawCategories = await _db.Notifications
            .Where(n => n.TenantId == tenantId && n.SentAt >= since && n.TemplateId != null)
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
