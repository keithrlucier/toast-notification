using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TemplatesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TemplateResponse>>> List()
    {
        var templates = await _db.NotificationTemplates
            .OrderBy(t => t.IsDefault ? 0 : 1)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(t => new TemplateResponse(
                t.Id,
                t.Name,
                ToSlug(t.Category),
                t.Category.ToString(),
                t.TitleTemplate,
                t.BodyLine1Template,
                t.BodyLine2Template,
                t.ActionButtonsJson,
                t.AudioSetting,
                t.Scenario.ToString().ToLowerInvariant(),
                t.IsDefault))
            .ToListAsync();

        return Ok(templates);
    }

    [HttpPost]
    public async Task<ActionResult<TemplateResponse>> Create([FromBody] CreateTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var template = new NotificationTemplate
        {
            TenantId       = tenantId,
            Name           = req.Name.Trim(),
            Category       = TemplateCategory.Custom,
            TitleTemplate  = req.Title?.Trim(),
            BodyLine1Template = req.BodyLine1?.Trim(),
            BodyLine2Template = req.BodyLine2?.Trim(),
            ActionButtonsJson = req.ActionButtonsJson,
            AudioSetting   = req.AudioSetting,
            Scenario       = ParseScenario(req.Scenario),
            IsDefault      = false,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        _db.NotificationTemplates.Add(template);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new TemplateResponse(
            template.Id,
            template.Name,
            "custom",
            "Custom",
            template.TitleTemplate,
            template.BodyLine1Template,
            template.BodyLine2Template,
            template.ActionButtonsJson,
            template.AudioSetting,
            template.Scenario.ToString().ToLowerInvariant(),
            false));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var template = await _db.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template is null) return NotFound();
        if (template.IsDefault) return BadRequest("Default templates cannot be deleted.");

        _db.NotificationTemplates.Remove(template);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenantId")?.Value ?? User.FindFirst("TenantId")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static ToastScenario ParseScenario(string? s) => s?.ToLowerInvariant() switch
    {
        "urgent"       => ToastScenario.Urgent,
        "reminder"     => ToastScenario.Reminder,
        "alarm"        => ToastScenario.Alarm,
        "incomingcall" => ToastScenario.IncomingCall,
        _              => ToastScenario.Default,
    };

    internal static string ToSlug(TemplateCategory category) => category switch
    {
        TemplateCategory.ActionRequired => "action-required",
        _ => category.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Seed the 6 default templates for a newly-created tenant.
    /// Called from AuthController.Register() inside the registration transaction.
    /// </summary>
    internal static IEnumerable<NotificationTemplate> BuildDefaultTemplates(Guid tenantId) =>
    [
        new() {
            TenantId = tenantId,
            Name = "Announcement",
            Category = TemplateCategory.Announcement,
            TitleTemplate = "Company Announcement",
            BodyLine1Template = "We have an important update for the team.",
            BodyLine2Template = "Please review the details at your earliest convenience.",
            AudioSetting = "ms-winsoundevent:Notification.Default",
            Scenario = ToastScenario.Default,
            IsDefault = true,
        },
        new() {
            TenantId = tenantId,
            Name = "Alert",
            Category = TemplateCategory.Alert,
            TitleTemplate = "Security Alert",
            BodyLine1Template = "Immediate action required on your device.",
            BodyLine2Template = "Please contact IT support or follow the link below.",
            AudioSetting = "ms-winsoundevent:Notification.Looping.Alarm",
            Scenario = ToastScenario.Urgent,
            IsDefault = true,
        },
        new() {
            TenantId = tenantId,
            Name = "Action Required",
            Category = TemplateCategory.ActionRequired,
            TitleTemplate = "Action Required",
            BodyLine1Template = "Your attention is required for an important task.",
            AudioSetting = "ms-winsoundevent:Notification.Reminder",
            Scenario = ToastScenario.Default,
            IsDefault = true,
        },
        new() {
            TenantId = tenantId,
            Name = "Reminder",
            Category = TemplateCategory.Reminder,
            TitleTemplate = "Reminder",
            BodyLine1Template = "This is a scheduled reminder.",
            AudioSetting = "ms-winsoundevent:Notification.Reminder",
            Scenario = ToastScenario.Reminder,
            IsDefault = true,
        },
        new() {
            TenantId = tenantId,
            Name = "Celebration",
            Category = TemplateCategory.Celebration,
            TitleTemplate = "Congratulations!",
            BodyLine1Template = "We have something exciting to share with the team.",
            AudioSetting = "ms-winsoundevent:Notification.Default",
            Scenario = ToastScenario.Default,
            IsDefault = true,
        },
        new() {
            TenantId = tenantId,
            Name = "Maintenance",
            Category = TemplateCategory.Maintenance,
            TitleTemplate = "Scheduled Maintenance",
            BodyLine1Template = "A maintenance window is scheduled.",
            BodyLine2Template = "Please save your work and plan accordingly.",
            AudioSetting = "ms-winsoundevent:Notification.Default",
            Scenario = ToastScenario.Default,
            IsDefault = true,
        },
    ];
}
