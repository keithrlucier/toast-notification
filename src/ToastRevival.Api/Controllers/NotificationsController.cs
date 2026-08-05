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
using ToastRevival.Api.Extensions;
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
    public async Task<ActionResult<NotificationResponse>> Send(
        [FromBody] SendNotificationRequest req,
        [FromServices] IConfiguration config)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var senderId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role     = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;

        // PERF-M4: consolidate both Tenants reads into one projection covering all needed fields.
        // (1) FIX-SES-2 — suspended tenant kill switch. (2) MFA enforcement gate.
        // (3) ModerationRequireApprovalAll and (4) ModerationBlockedMessage for moderation path below.
        var tenantGate = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.RequireMfa,
                Suspended = t.SuspendedAt != null,
                t.ModerationRequireApprovalAll,
                t.ModerationBlockedMessage
            })
            .FirstOrDefaultAsync();
        if (tenantGate?.Suspended == true)
            return StatusCode(403, new
            {
                error = "tenant_suspended",
                message = "This organization's access is suspended."
            });
        if (tenantGate?.RequireMfa == true && !User.HasFreshMfa(config))
            return StatusCode(403, new
            {
                error = "mfa_required",
                message = "Sending a notification requires MFA verification. Verify your authenticator and try again."
            });

        // Expand targets to device IDs
        var deviceIds = await ResolveTargetDeviceIds(req, tenantId);
        if (deviceIds.Count == 0 && req.TargetType != TargetType.All)
            return BadRequest("No devices matched the specified targets.");

        // D5 Broadcast gate: >100 devices requires Admin+
        if (deviceIds.Count > 100 && role < UserRole.Admin)
            return StatusCode(403, new { error = "insufficient_role", message = "Sending to >100 devices requires Admin role." });

        // D5/D6 + FIX-MFA-2 (2026-06-01): a fleet-scale broadcast requires a fresh MFA
        // step-up — gated on the *resolved blast radius*, not just the TargetType enum.
        // Gating only on TargetType.All let a caller reach the whole fleet un-stepped-up
        // by listing every device id as Device/Group targets (same end state, different
        // enum). The >100 threshold mirrors the Admin-role broadcast gate directly above.
        if (req.TargetType == TargetType.All || deviceIds.Count > 100)
        {
            if (!User.HasFreshMfa(config))
                return StatusCode(403, new
                {
                    error = "mfa_required",
                    message = "Broadcasting to many devices requires MFA verification. Verify your authenticator and try again."
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

        // M11: per-tenant "require approval for all" override. When this is on, every
        // notification routes to PendingReview regardless of moderation engine output,
        // unless it was Block (which stays Block — admin approval is for review-tier
        // content, not for content the tenant policy already rejected).
        // PERF-M4: use values already fetched in the tenantGate projection above.
        var requireApprovalAll = tenantGate?.ModerationRequireApprovalAll ?? false;
        var blockedMessage = tenantGate?.ModerationBlockedMessage;

        if (requireApprovalAll && moderationResult.Decision == ModerationDecision.Pass)
        {
            moderationResult = moderationResult with { Decision = ModerationDecision.Review };
        }

        // Short-circuit on Block — do not persist, return 422
        if (moderationResult.Decision == ModerationDecision.Block)
        {
            return UnprocessableEntity(new
            {
                error = "content_blocked",
                message = blocklistHit is not null
                    ? $"Content blocked by tenant blocklist (matched term: '{blocklistHit.BlocklistTerm}')."
                    : !string.IsNullOrWhiteSpace(blockedMessage)
                        ? blockedMessage
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

        // REL-002-R: check return value — a false means the bounded channel is full.
        // Return 503 so the caller knows to retry; do NOT return 2xx for an un-enqueued
        // notification (the startup recovery sweep will pick it up on restart, but that
        // can be many seconds away and the caller deserves an honest response now).
        if (initialStatus == NotificationStatus.Queued &&
            (req.ScheduledAt is null || req.ScheduledAt <= DateTime.UtcNow))
        {
            if (!_queue.Enqueue(notification.Id))
                return StatusCode(503, new { error = "queue_full", message = "Notification queue is at capacity. Retry shortly." });
        }

        return Accepted(new NotificationResponse(
            notification.Id, notification.Title, notification.BodyLine1,
            notification.BodyLine2, notification.Status.ToString(),
            notification.TargetType.ToString(), notification.TargetDeviceCount,
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
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null)
    {
        // SES-4: This is a user-facing endpoint. A device JWT (type=device) would
        // satisfy the controller-level [Authorize] but must not read tenant-wide
        // notification history — reject token-type confusion.
        if (User.FindFirstValue("type") == "device")
            return StatusCode(403, new { error = "forbidden", message = "Device tokens cannot access notification history." });

        var p    = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);

        // MT-H2: Explicit TenantId predicate as defense-in-depth alongside EF global filter.
        // REVIEW-2026-06-06 REST-M5 REJECTED-by-design: pagination envelope would break existing dashboard client expecting bare array; coordinated API+frontend change filed as REST-refactor milestone
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        var items = await _db.Notifications
            .Where(n => n.TenantId == tenantId)
            .Where(n => search == null || EF.Functions.ILike(n.Title, $"%{search}%"))
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
        // SES-4 (Routes-M1): a device JWT carries tenantId and satisfies [Authorize], but must
        // not read a tenant's notification content — mirror History's device-token rejection.
        if (User.FindFirstValue("type") == "device")
            return StatusCode(403, new { error = "forbidden", message = "Device tokens cannot access notification details." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var n = await _db.Notifications
            .FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId);
        if (n is null) return NotFound();

        return Ok(new NotificationResponse(
            n.Id, n.Title, n.BodyLine1, n.BodyLine2,
            n.Status.ToString(), n.TargetType.ToString(), n.TargetDeviceCount,
            n.ScheduledAt, n.SentAt, n.CreatedAt));
    }

    /// <summary>
    /// Device-authenticated catch-up endpoint. Returns the same signed
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

        // SES-3: a decommissioned device's 365-day JWT stays cryptographically
        // valid; reject it here (mirrors NotificationHub.OnConnectedAsync).
        if (await IsDeviceRevoked(deviceId))
            return Unauthorized();

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

        // SES-3: reject decommissioned-device tokens (mirrors the hub).
        if (await IsDeviceRevoked(deviceId))
            return Unauthorized();

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
        // XT-4: dashboard recon events go to the user-only dashboard-{tenantId} group,
        // never tenant-{id} (devices are in tenant-{id} and must not harvest this stream).
        await _hub.Clients.Group($"dashboard-{tenantId}")
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
        // SES-4 (Routes-M1): reject device JWTs — a device must not download the tenant-wide
        // per-device delivery report (DeviceName/DeviceId/Status/timestamps). Mirrors History.
        if (User.FindFirstValue("type") == "device")
            return StatusCode(403, new { error = "forbidden", message = "Device tokens cannot access delivery reports." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId);
        if (notification is null) return NotFound();

        // MT-M5: Add explicit TenantId filter on child deliveries (parent Notification
        // already filtered by TenantId above, but child rows need their own predicate).
        var deliveries = await _db.NotificationDeliveries
            .Include(d => d.Device)
            .Where(d => d.NotificationId == id && d.TenantId == tenantId)
            .OrderBy(d => d.DeliveredAt ?? d.CreatedAt)
            .ToListAsync();

        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

        var shortId = id.ToString()[..8];

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfBytes = await _pdf.GenerateDeliveryReportPdfAsync(notification, deliveries, tenantName);
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

    // INJ-M3: Prefix formula-trigger characters to prevent CSV injection.
    private static string EscapeCsvTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;
        if (title[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            title = "'" + title;
        return title.Replace("\"", "\"\"");
    }

    // INJ-M4: Exposed as internal static so TemplatesController can reuse the same validation.
    internal static bool TryNormalizeActionButtonsJson(string json, out string? normalizedJson, out string? error)
    {
        try
        {
            var element = JsonDocument.Parse(json).RootElement;
            return TryNormalizeActionButtons(element, out normalizedJson, out error);
        }
        catch
        {
            normalizedJson = null;
            error = "Action buttons must be valid JSON.";
            return false;
        }
    }

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

    /// <summary>
    /// ARCH-M2: Delegates to the shared DbContextExtensions.IsDeviceRevokedAsync.
    /// </summary>
    private Task<bool> IsDeviceRevoked(Guid deviceId) =>
        _db.IsDeviceRevokedAsync(deviceId);

    private async Task<List<Guid>> ResolveTargetDeviceIds(SendNotificationRequest req, Guid tenantId)
    {
        // MT-H5: Explicit TenantId predicates on All and Device paths to prevent
        // cross-tenant device targeting if global query filter is bypassed.
        IQueryable<Device> query = _db.Devices.Where(d => d.TenantId == tenantId && d.Status == DeviceStatus.Active);

        return req.TargetType switch
        {
            // MT-H5: TenantId already in base query above.
            TargetType.All => await query.Select(d => d.Id).ToListAsync(),

            // MT-H5: TenantId already in base query above.
            TargetType.Device when req.TargetIds?.Count > 0 =>
                await query.Where(d => req.TargetIds.Contains(d.Id))
                           .Select(d => d.Id).ToListAsync(),

            // MT-H5/AA-M9: TargetType.Group already correct — both sides carry TenantId.
            TargetType.Group when req.TargetIds?.Count > 0 =>
                await _db.DeviceGroupMembers
                    .Where(m => req.TargetIds.Contains(m.DeviceGroupId)
                        && m.DeviceGroup.TenantId == tenantId
                        && m.Device.TenantId == tenantId
                        && m.Device.Status == DeviceStatus.Active)
                    .Select(m => m.DeviceId).Distinct().ToListAsync(),

            _ => [],
        };
    }
}
