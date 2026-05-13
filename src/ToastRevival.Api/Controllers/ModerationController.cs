using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// Admin approval queue for notifications flagged by the moderation engine (D4).
/// Notifications with Status=PendingReview were not enqueued — they sit here
/// until an Admin approves (→ Queued + enqueued) or rejects (→ Failed).
/// No UI yet (M4 dashboard); these endpoints are the backend contract.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class ModerationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationQueueService _queue;
    private readonly IAuditService _audit;

    public ModerationController(AppDbContext db, INotificationQueueService queue, IAuditService audit)
    {
        _db    = db;
        _queue = queue;
        _audit = audit;
    }

    /// <summary>Returns all PendingReview notifications for this tenant.</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<PendingReviewItem>>> GetPending()
    {
        if (!IsAdminOrAbove()) return Forbid();

        var items = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.PendingReview)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new PendingReviewItem(
                n.Id,
                n.Title,
                n.BodyLine1,
                n.BodyLine2,
                n.HeroImageUrl,
                n.ModerationResultJson,
                n.CreatedAt,
                n.Sender.Email!))
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>Approves a flagged notification — sets it to Queued and enqueues for delivery.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ModerationActionRequest? req = null)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null) return NotFound();
        if (notification.Status != NotificationStatus.PendingReview)
            return BadRequest("Notification is not pending review.");

        notification.Status = NotificationStatus.Queued;
        await _db.SaveChangesAsync();

        _queue.Enqueue(notification.Id);

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "moderation.approve", "Notification", id.ToString(),
            new { reason = req?.Reason }, HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    /// <summary>Rejects a flagged notification — sets it to Failed, permanently blocked.</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ModerationActionRequest? req = null)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null) return NotFound();
        if (notification.Status != NotificationStatus.PendingReview)
            return BadRequest("Notification is not pending review.");

        notification.Status     = NotificationStatus.Failed;
        notification.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "moderation.reject", "Notification", id.ToString(),
            new { reason = req?.Reason }, HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    private bool IsAdminOrAbove()
    {
        var role = User.FindFirstValue("role");
        return role is nameof(UserRole.Admin) or nameof(UserRole.SuperAdmin);
    }
}
