using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuditService _audit;

    private readonly ILicenseService _license;
    private readonly IStripeBillingSyncService _billingSync;

    public DevicesController(
        AppDbContext db,
        ITokenService tokens,
        IAuditService audit,
        ILicenseService license,
        IStripeBillingSyncService billingSync)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
        _license = license;
        _billingSync = billingSync;
    }

    /// <summary>
    /// Called by the agent on first run. No authentication required.
    /// TenantId comes from the MSI property set by the MSP during deployment.
    ///
    /// Enrollment key gating (INFO-M1-003): when a tenant has an EnrollmentKey
    /// set, the request must include the matching key or registration is rejected
    /// with 403. Tenants without an EnrollmentKey allow open registration.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<ActionResult<DeviceTokenResponse>> Register([FromBody] RegisterDeviceRequest req)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == req.TenantId);
        if (tenant is null) return NotFound("Tenant not found.");

        if (!string.IsNullOrWhiteSpace(tenant.EnrollmentKey))
        {
            if (string.IsNullOrWhiteSpace(req.EnrollmentKey) ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(req.EnrollmentKey),
                    System.Text.Encoding.UTF8.GetBytes(tenant.EnrollmentKey)))
            {
                return StatusCode(403, "Invalid enrollment key.");
            }
        }

        // M6 D3: license enforcement — block registration when limit reached or billing canceled
        if (!await _license.CanRegisterDeviceAsync(req.TenantId))
        {
            return StatusCode(403, "Subscription canceled. Please renew to register devices.");
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = HashToken(rawToken);

        var device = new Device
        {
            TenantId = req.TenantId,
            DeviceName = req.DeviceName,
            Username = req.Username,
            OsVersion = req.OsVersion,
            AgentVersion = req.AgentVersion,
            RegistrationToken = tokenHash,
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        // M6 D4: maintain ConsumedCount
        await _license.IncrementConsumedAsync(tenant);
        await _billingSync.SyncSubscriptionQuantityAsync(tenant);

        var jwt = _tokens.CreateDeviceToken(device);

        await _audit.LogAsync(req.TenantId, null, "device.register", "Device",
            device.Id.ToString(), new { device.DeviceName, device.Username },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new DeviceTokenResponse(jwt, device.Id, req.TenantId, tenant.SigningKey, tenant.Name));
    }

    [Authorize]
    [HttpGet]
    [EnableRateLimiting("tenant-per-minute")]
    public async Task<ActionResult<IEnumerable<DeviceResponse>>> List()
    {
        var devices = await _db.Devices
            .Where(d => d.Status != DeviceStatus.Decommissioned)
            .Select(d => ToResponse(d))
            .ToListAsync();

        return Ok(devices);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceResponse>> Get(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        return device is null ? NotFound() : Ok(ToResponse(device));
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Decommission(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device is null) return NotFound();

        device.Status = DeviceStatus.Decommissioned;
        await _db.SaveChangesAsync();

        // M6 D4: maintain ConsumedCount
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is not null)
        {
            await _license.DecrementConsumedAsync(tenant);
            await _billingSync.SyncSubscriptionQuantityAsync(tenant);
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _audit.LogAsync(tenantId, userId, "device.decommission", "Device", id.ToString());

        return NoContent();
    }

    /// <summary>
    /// Returns the current tenant display name for notification attribution.
    /// Called by the agent on every startup so toasts reflect the latest name
    /// even if the admin renamed the tenant after the device registered.
    /// Device-JWT only (requires "deviceId" claim).
    /// </summary>
    [Authorize]
    [HttpGet("tenant-name")]
    public async Task<ActionResult<TenantAttributionResponse>> GetTenantName()
    {
        var deviceIdClaim = User.FindFirstValue("deviceId");
        if (string.IsNullOrEmpty(deviceIdClaim)) return Unauthorized();

        var tenantIdClaim = User.FindFirstValue("tenantId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId)) return Unauthorized();

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        return Ok(new TenantAttributionResponse(tenant.Name));
    }

    // Called by agent to confirm it's still alive (heartbeat)
    [Authorize]
    [HttpPost("ping")]
    [EnableRateLimiting("device-per-hour")]
    public async Task<IActionResult> Ping()
    {
        var deviceIdClaim = User.FindFirstValue("deviceId");
        if (!Guid.TryParse(deviceIdClaim, out var deviceId)) return Unauthorized();

        var device = await _db.Devices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return NotFound();

        device.LastPing = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static DeviceResponse ToResponse(Device d) =>
        new(d.Id, d.DeviceName, d.Username, d.OsVersion, d.AgentVersion,
            d.Status.ToString(), d.LastPing, d.RegisteredAt);

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }
}
