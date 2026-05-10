using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class TenantController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantController(AppDbContext db) => _db = db;

    [HttpGet("settings")]
    public async Task<ActionResult<TenantSettingsResponse>> GetSettings()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        // INFO-M1-003 (M9.C): EnrollmentKey only surfaces to admins — Technicians
        // get null. The key must be paste-able into RMM/Intune deploy scripts but
        // shouldn't show in the read-only settings view of a junior tech.
        var enrollmentKey = IsAdmin() ? tenant.EnrollmentKey : null;

        return Ok(new TenantSettingsResponse(
            TenantName:          tenant.Name,
            LogoUrl:             tenant.LogoUrl,
            PrimaryColor:        tenant.PrimaryColor,
            DefaultAudioSetting: tenant.DefaultAudioSetting,
            DefaultScenario:     tenant.DefaultScenario.ToString(),
            RateLimitPerMinute:  60,
            RateLimitPerHour:    500,
            RateLimitPerDay:     5000,
            EnrollmentKey:       enrollmentKey));
    }

    /// <summary>
    /// INFO-M1-003 (M9.C): admin-only regenerate of the per-tenant enrollment key.
    /// Returns the new key. After rotation, every existing v0.3.2+ agent install
    /// continues to work (the key is only checked at /api/devices/register, not
    /// on the device JWT it already holds), but any new MSI deploy must use the
    /// new key — old MSIs with the prior key in their bootstrap will be 403'd.
    /// </summary>
    [HttpPost("enrollment-key/regenerate")]
    public async Task<ActionResult<EnrollmentKeyResponse>> RegenerateEnrollmentKey()
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        tenant.EnrollmentKey = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        await _db.SaveChangesAsync();

        return Ok(new EnrollmentKeyResponse(tenant.EnrollmentKey));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateTenantSettingsRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        tenant.LogoUrl             = string.IsNullOrWhiteSpace(req.LogoUrl)             ? null : req.LogoUrl.Trim();
        tenant.PrimaryColor        = string.IsNullOrWhiteSpace(req.PrimaryColor)        ? null : req.PrimaryColor.Trim();
        tenant.DefaultAudioSetting = string.IsNullOrWhiteSpace(req.DefaultAudioSetting) ? null : req.DefaultAudioSetting;

        if (req.DefaultScenario is not null
            && Enum.TryParse<ToastScenario>(req.DefaultScenario, ignoreCase: true, out var scenario))
            tenant.DefaultScenario = scenario;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    private bool IsAdmin()
    {
        var role = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;
        return role >= UserRole.Admin;
    }
}
