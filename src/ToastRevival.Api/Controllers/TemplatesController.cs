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
            .OrderBy(t => t.Category)
            .Select(t => new TemplateResponse(
                t.Id,
                t.Name,
                ToSlug(t.Category),
                t.Category.ToString(),
                t.TitleTemplate,
                t.BodyLine1Template,
                t.BodyLine2Template,
                t.AudioSetting,
                t.Scenario.ToString().ToLowerInvariant(),
                t.IsDefault))
            .ToListAsync();

        return Ok(templates);
    }

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
