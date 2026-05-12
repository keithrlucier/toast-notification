using System.Security.Claims;
using System.Text;
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
using ToastRevival.Api.Utilities;

// Severity thresholds (D3): 0-1=Pass, 2-4=Review, 5-6=Block.

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
    private readonly IContentModerationService _moderation;
    private readonly IBlocklistService _blocklist;
    private readonly IPdfExportService _pdf;

    public NotificationsController(
        AppDbContext db,
        INotificationQueueService queue,
        IAuditService audit,
        IHubContext<NotificationHub> hub,
        IContentModerationService moderation,
        IBlocklistService blocklist,
        IPdfExportService pdf)
    {
        _db = db;
        _queue = queue;
        _audit = audit;
        _hub = hub;
        _moderation = moderation;
        _blocklist = blocklist;
        _pdf = pdf;
    }

    [HttpPost]
    public async Task<ActionResult<NotificationResponse>> Send([FromBody] SendNotificationRequest req)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var senderId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role     = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;

        // Expand targets to device IDs
        var deviceIds = await ResolveTargetDeviceIds(req, tenantId);
        if (deviceIds.Count == 0 && req.TargetType != TargetType.All)
            return BadRequest("No devices matched the specified targets.");

        // D5 Broadcast gate: >100 devices requires Admin+
        if (deviceIds.Count > 100 && role < UserRole.Admin)
            return StatusCode(403, new { error = "insufficient_role", message = "Sending to >100 devices requires Admin role." });

        // D5/D6: Broadcast-to-all requires an MFA-elevated JWT (mfa=true claim)
        if (req.TargetType == TargetType.All)
        {
            if (User.FindFirstValue("mfa") != "true")
                return StatusCode(403, new
                {
                    error = "mfa_required",
                    message = "Broadcasting to all devices requires MFA verification. Call POST /api/auth/mfa/verify first."
                });
        }

        if (!TryNormalizeActionButtons(req.ActionButtons, out var actionButtonsJson, out var actionButtonsError))
        {
            return BadRequest(actionButtonsError);
        }

        // D7 Blocklist check — fast, in-process, happens before any external call
        var blocklistHit = await _blocklist.CheckAsync(req.Title, req.BodyLine1, req.BodyLine2);

        ModerationResult moderationResult;
        if (blocklistHit is not null)
        {
            moderationResult = blocklistHit;
        }
        else
        {
            // D1/D2 Azure Content Safety scan
            var textResult = await _moderation.ModerateTextAsync(req.Title, req.BodyLine1, req.BodyLine2);

            // Image scan only for ad-hoc URLs (skip library assets — no AssetLibrary lookup yet, M5)
            var imageResult = !string.IsNullOrWhiteSpace(req.HeroImageUrl)
                ? await _moderation.ModerateImageUrlAsync(req.HeroImageUrl)
                : new ModerationResult(ModerationDecision.Pass, null, null, null);

            // D3 Aggregate: take the worst decision
            moderationResult = AggregateModerationResults(textResult, imageResult);
        }

        // Short-circuit on Block — do not persist, return 422
        if (moderationResult.Decision == ModerationDecision.Block)
        {
            return UnprocessableEntity(new
            {
                error = "content_blocked",
                message = blocklistHit is not null
                    ? $"Content blocked by tenant blocklist (matched term: '{blocklistHit.BlocklistTerm}')."
                    : "Content blocked by moderation policy.",
                scores = moderationResult.TextScores,
            });
        }

        var moderationJson = JsonSerializer.Serialize(new
        {
            decision = moderationResult.Decision.ToString(),
            textScores = moderationResult.TextScores,
            imageScores = moderationResult.ImageScores,
            blocklistTerm = moderationResult.BlocklistTerm,
        });

        // PendingReview: save but do NOT enqueue; admin must approve via /api/moderation/{id}/approve
        var initialStatus = moderationResult.Decision == ModerationDecision.Review
            ? NotificationStatus.PendingReview
            : NotificationStatus.Queued;

        // Default per-notification LogoUrl to the tenant's configured logo when
        // the sender didn't supply one. TenantSettings.UploadLogo persists the
        // tenant logo with the documented intent that it be "used as the
        // notification icon" — without this fallback the field was wired to the
        // database but never reached the wire, so Windows fell back to the
        // agent's static Assets\\toast-logo.png.
        var resolvedLogoUrl = !string.IsNullOrWhiteSpace(req.LogoUrl)
            ? req.LogoUrl
            : await _db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => t.LogoUrl)
                .FirstOrDefaultAsync();

        var notification = new Notification
        {
            TenantId = tenantId,
            TemplateId = req.TemplateId,
            SenderId = senderId,
            Title = req.Title,
            BodyLine1 = req.BodyLine1,
            BodyLine2 = req.BodyLine2,
            HeroImageUrl = req.HeroImageUrl,
            LogoUrl = resolvedLogoUrl,
            ActionButtonsJson = actionButtonsJson,
            AudioSetting = req.AudioSetting,
            Scenario = req.Scenario,
            TargetType = req.TargetType,
            TargetIdsJson = req.TargetIds is not null
                ? JsonSerializer.Serialize(req.TargetIds) : null,
            TargetDeviceCount = deviceIds.Count,
            ScheduledAt = req.ScheduledAt,
            ModerationResultJson = moderationJson,
            Status = initialStatus,
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
            new { req.Title, req.TargetType, deviceCount = deviceIds.Count, moderation = moderationResult.Decision.ToString() },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        // Only enqueue if content passed moderation and is not future-scheduled
        if (initialStatus == NotificationStatus.Queued &&
            (req.ScheduledAt is null || req.ScheduledAt <= DateTime.UtcNow))
        {
            _queue.Enqueue(notification.Id);
        }

        return Accepted(new NotificationResponse(
            notification.Id, notification.Title, notification.BodyLine1,
            notification.BodyLine2, notification.Status.ToString(),
            notification.TargetType, notification.TargetDeviceCount,
            notification.ScheduledAt, notification.SentAt, notification.CreatedAt));
    }

    private static ModerationResult AggregateModerationResults(ModerationResult text, ModerationResult image)
    {
        // Merge scores and take the worst decision
        var mergedText  = text.TextScores;
        var mergedImage = image.ImageScores;
        var worst = text.Decision >= image.Decision ? text.Decision : image.Decision;
        return new ModerationResult(worst, mergedText, mergedImage, null);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationHistoryItem>>> History(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var p    = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);

        var items = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Skip((p - 1) * size)
            .Take(size)
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
    /// Device-authenticated catch-up endpoint (M2.B). Returns the same signed
    /// payload + signature pairs the hub fanout would have pushed for any
    /// Pending deliveries the agent missed while disconnected. Agent calls this
    /// on every Reconnected event and once after cold StartAsync.
    ///
    /// Filtering: `delivery.DeviceId == me AND delivery.TenantId == me AND
    /// delivery.Status == Pending`, optionally bounded by `since` against
    /// delivery.CreatedAt (which is set when the delivery row was created at
    /// /api/notifications send time). Response capped at `limit` items per call,
    /// where `limit` defaults to 100 (backwards compat for v0.3.x agents) and
    /// is clamped to [1, 500] — a device that has been offline for a long
    /// time pages on subsequent calls since its remaining Pending deliveries
    /// will still be Pending after the first batch is reported delivered.
    ///
    /// Authorization: device-JWT only (type=device claim). User JWTs are
    /// rejected even though the controller-level [Authorize] would let them
    /// through — the response contains payloads scoped to a specific device.
    /// Rate limit overridden to device-per-hour.
    /// </summary>
    [HttpGet("pending")]
    [EnableRateLimiting("device-catchup-per-hour")]
    public async Task<ActionResult<IEnumerable<PendingNotificationItem>>> GetPending(
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);

        var typeClaim = User.FindFirstValue("type");
        var deviceIdClaim = User.FindFirstValue("deviceId");
        var tenantIdClaim = User.FindFirstValue("tenantId");

        if (typeClaim != "device"
            || !Guid.TryParse(deviceIdClaim, out var deviceId)
            || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized();
        }

        // The tenant filter on Tenants is keyed off ITenantProvider, which reads
        // tenantId from the JWT — so the filtered query would work, but we use
        // IgnoreQueryFilters() to be explicit and parallel to the rest of the
        // device-context paths that all bypass filters.
        var signingKey = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.SigningKey)
            .FirstOrDefaultAsync();
        if (signingKey is null) return Unauthorized();

        var query = _db.NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.DeviceId == deviceId
                     && d.TenantId == tenantId
                     && d.Status == DeliveryStatus.Pending);

        if (since.HasValue)
            query = query.Where(d => d.CreatedAt >= since.Value);

        var pending = await query
            .Include(d => d.Notification)
            .OrderBy(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var items = pending.Select(d =>
        {
            var (payloadJson, signature) = NotificationPayloadBuilder.BuildSigned(d.Notification, signingKey);
            return new PendingNotificationItem(d.NotificationId, payloadJson, signature, d.CreatedAt);
        }).ToList();

        return Ok(items);
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

    /// <summary>
    /// GET /api/notifications/{id}/report?format=csv|pdf
    /// Downloads a per-notification delivery report — one row per target device.
    /// Available to all authenticated tenant users (not admin-only; standard MSP workflow).
    /// </summary>
    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> DeliveryReport(Guid id, [FromQuery] string format = "csv")
    {
        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null) return NotFound();

        var deliveries = await _db.NotificationDeliveries
            .Include(d => d.Device)
            .Where(d => d.NotificationId == id)
            .OrderBy(d => d.DeliveredAt ?? d.CreatedAt)
            .ToListAsync();

        var tenantId   = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

        var shortId = id.ToString()[..8];

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfBytes = _pdf.GenerateDeliveryReportPdf(notification, deliveries, tenantName);
            return File(pdfBytes, "application/pdf", $"delivery-{shortId}.pdf");
        }

        var csv      = BuildDeliveryCsv(notification, deliveries);
        var csvBytes = Encoding.UTF8.GetBytes(csv);
        return File(csvBytes, "text/csv", $"delivery-{shortId}.csv");
    }

    private static string BuildDeliveryCsv(Notification notification, IList<NotificationDelivery> deliveries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Delivery Report — {EscapeCsvTitle(notification.Title)}");
        sb.AppendLine($"# Notification ID: {notification.Id}");
        sb.AppendLine($"# Sent: {notification.SentAt?.ToString("o") ?? "N/A"}");
        sb.AppendLine();
        sb.AppendLine("DeviceName,DeviceId,Status,DeliveredAt,InteractedAt,Action,Error");

        foreach (var d in deliveries)
        {
            sb.AppendLine(string.Join(",",
                CsvHelper.Cell(d.Device?.DeviceName ?? ""),
                CsvHelper.Cell(d.DeviceId.ToString()),
                CsvHelper.Cell(d.Status.ToString()),
                CsvHelper.Cell(d.DeliveredAt?.ToString("o") ?? ""),
                CsvHelper.Cell(d.InteractedAt?.ToString("o") ?? ""),
                CsvHelper.Cell(d.Action ?? ""),
                CsvHelper.Cell(d.ErrorMessage ?? "")));
        }

        return sb.ToString();
    }

    private static string EscapeCsvTitle(string value) => value.Replace("\"", "\"\"");

    private static bool TryNormalizeActionButtons(object? rawButtons, out string? normalizedJson, out string? error)
    {
        normalizedJson = null;
        error = null;

        if (rawButtons is null) return true;

        JsonElement root;
        try
        {
            root = rawButtons is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(rawButtons);
        }
        catch
        {
            error = "Action buttons must be a JSON array.";
            return false;
        }

        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        if (root.ValueKind != JsonValueKind.Array)
        {
            error = "Action buttons must be a JSON array.";
            return false;
        }

        var normalized = new List<Dictionary<string, object?>>();
        var index = 0;

        foreach (var item in root.EnumerateArray())
        {
            index++;
            if (index > 3)
            {
                error = "A notification can include at most 3 action buttons.";
                return false;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                error = $"Action button {index} must be an object.";
                return false;
            }

            var label = GetString(item, "label")?.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                error = $"Action button {index} needs a label.";
                return false;
            }
            if (label.Length > 32)
            {
                error = $"Action button {index} label must be 32 characters or fewer.";
                return false;
            }

            var actionId = GetString(item, "actionId")?.Trim();
            if (string.IsNullOrWhiteSpace(actionId))
                actionId = GetString(item, "action")?.Trim();
            if (string.IsNullOrWhiteSpace(actionId))
                actionId = $"button_{index}";
            if (actionId.Length > 64)
            {
                error = $"Action button {index} action ID must be 64 characters or fewer.";
                return false;
            }

            var style = NormalizeButtonStyle(GetString(item, "style"));
            var type = NormalizeButtonType(GetString(item, "type"));
            var url = GetString(item, "url")?.Trim();
            if (!string.IsNullOrWhiteSpace(url)) type = "Url";

            var button = new Dictionary<string, object?>
            {
                ["label"] = label,
                ["actionId"] = actionId,
                ["action"] = actionId,
                ["style"] = style,
                ["type"] = type,
            };

            if (type == "Url")
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    error = $"Action button {index} URL is required.";
                    return false;
                }
                if (url.Length > 2048)
                {
                    error = $"Action button {index} URL must be 2048 characters or fewer.";
                    return false;
                }
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                    || parsed.Scheme is not ("http" or "https"))
                {
                    error = $"Action button {index} URL must be an absolute http:// or https:// URL.";
                    return false;
                }

                button["url"] = parsed.AbsoluteUri;
            }

            normalized.Add(button);
        }

        normalizedJson = normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
        return true;
    }

    private static string? GetString(JsonElement item, string name)
    {
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }

    private static string NormalizeButtonStyle(string? value) =>
        string.Equals(value, "Success", StringComparison.OrdinalIgnoreCase) ? "Success" :
        string.Equals(value, "Critical", StringComparison.OrdinalIgnoreCase) ? "Critical" :
        "Default";

    private static string NormalizeButtonType(string? value) =>
        string.Equals(value, "Url", StringComparison.OrdinalIgnoreCase) ? "Url" : "Action";

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
