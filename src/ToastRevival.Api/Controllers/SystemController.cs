using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize(Policy = "PlatformAdmin")]
[EnableRateLimiting("tenant-per-minute")]
public class SystemController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBillingConfigService _billingConfig;
    private readonly IMessagingConfigService _messagingConfig;
    private readonly ISsoConfigService _ssoConfig;
    private readonly IAuditService _audit;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<SystemController> _logger;

    public SystemController(
        AppDbContext db,
        IBillingConfigService billingConfig,
        IMessagingConfigService messagingConfig,
        ISsoConfigService ssoConfig,
        IAuditService audit,
        UserManager<AppUser> userManager,
        IEmailService email,
        IConfiguration config,
        ILogger<SystemController> logger)
    {
        _db = db;
        _billingConfig = billingConfig;
        _messagingConfig = messagingConfig;
        _ssoConfig = ssoConfig;
        _audit = audit;
        _userManager = userManager;
        _email = email;
        _config = config;
        _logger = logger;
    }

    [HttpGet("trial-requests")]
    public async Task<IActionResult> TrialRequests([FromQuery] TrialRequestStatus status = TrialRequestStatus.Pending)
    {
        var rows = await _db.TrialRequests
            .AsNoTracking()
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(100)
            .ToListAsync();

        var requests = rows.Select(r => new
        {
            r.Id,
            r.CompanyName,
            r.Website,
            r.FullName,
            r.Email,
            r.Phone,
            r.JobTitle,
            intendedUseCase = r.IntendedUseCase.ToString(),
            r.IntendedUseCaseDetails,
            status = r.Status.ToString(),
            r.SubmittedAt,
            r.ReviewedAt,
            r.ReviewedByUserId,
            r.ReviewNote,
            r.CreatedTenantId,
            r.CreatedUserId,
            r.RemoteIpAddress,
            r.UserAgent,
            r.TurnstileHostname,
            r.TurnstileAction,
        });

        return Ok(new { requests });
    }

    [HttpPost("trial-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveTrialRequest(Guid id, [FromBody] ReviewTrialRequestRequest? request)
    {
        var trial = await _db.TrialRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (trial is null) return NotFound();
        if (trial.Status != TrialRequestStatus.Pending)
            return Conflict("Trial request has already been reviewed.");

        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == trial.Email))
            return Conflict("An account with that email already exists.");

        var trialDays = request?.TrialDays is > 0 and <= 60
            ? request.TrialDays.Value
            : Math.Clamp(_config.GetValue<int?>("Registration:TrialDays") ?? 14, 1, 60);

        using var tx = await _db.Database.BeginTransactionAsync();

        var subdomain = await AllocateSubdomainAsync(trial.CompanyName);
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Name = trial.CompanyName,
            Subdomain = subdomain,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            EnrollmentKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            BillingStatus = BillingStatus.Trialing,
            LicenseStart = now,
            LicenseEnd = now.AddDays(trialDays),
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var user = new AppUser
        {
            TenantId = tenant.Id,
            FullName = trial.FullName,
            Email = trial.Email,
            UserName = trial.Email,
            PhoneNumber = trial.Phone,
            Role = UserRole.SuperAdmin,
            SecurityStamp = Guid.NewGuid().ToString(),
            RegistrationStep = RegistrationStep.PendingPasswordSet,
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });
        }

        foreach (var template in TemplatesController.BuildDefaultTemplates(tenant.Id))
            _db.NotificationTemplates.Add(template);

        trial.Status = TrialRequestStatus.Approved;
        trial.ReviewedAt = now;
        trial.ReviewedByUserId = GetUserId();
        trial.ReviewNote = string.IsNullOrWhiteSpace(request?.Note) ? null : request.Note.Trim();
        trial.CreatedTenantId = tenant.Id;
        trial.CreatedUserId = user.Id;

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _audit.LogAsync(
            GetTenantId(),
            GetUserId(),
            "trial_request.approved",
            "TrialRequest",
            trial.Id.ToString(),
            new { tenantId = tenant.Id, userId = user.Id, trial.Email, trialDays },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        var emailSent = await SendTrialApprovedEmailAsync(user, tenant);

        return Ok(new
        {
            trialRequestId = trial.Id,
            tenantId = tenant.Id,
            userId = user.Id,
            emailSent,
        });
    }

    [HttpPost("trial-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectTrialRequest(Guid id, [FromBody] ReviewTrialRequestRequest? request)
    {
        var trial = await _db.TrialRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (trial is null) return NotFound();
        if (trial.Status != TrialRequestStatus.Pending)
            return Conflict("Trial request has already been reviewed.");

        trial.Status = TrialRequestStatus.Rejected;
        trial.ReviewedAt = DateTime.UtcNow;
        trial.ReviewedByUserId = GetUserId();
        trial.ReviewNote = string.IsNullOrWhiteSpace(request?.Note) ? null : request.Note.Trim();
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(),
            GetUserId(),
            "trial_request.rejected",
            "TrialRequest",
            trial.Id.ToString(),
            new { trial.Email },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { trialRequestId = trial.Id, status = trial.Status.ToString() });
    }

    [HttpGet("messaging/config")]
    public IActionResult MessagingConfig()
    {
        return Ok(_messagingConfig.GetSnapshot());
    }

    [HttpPost("messaging/config")]
    public async Task<IActionResult> UpdateMessagingConfig([FromBody] UpdateMessagingConfigRequest request)
    {
        if (request is null)
            return BadRequest(new { message = "Messaging configuration is required." });

        var snapshot = await _messagingConfig.UpdateAsync(
            request.ClickSendUsername,
            request.ClickSendApiKey,
            request.MailjetApiKey,
            request.MailjetApiSecret,
            request.MailjetSenderEmail,
            HttpContext.RequestAborted);

        await _audit.LogAsync(
            GetTenantId(),
            GetUserId(),
            "messaging.config.updated",
            "SystemMessagingConfig",
            null,
            new { updatedFields = new[] {
                request.ClickSendUsername  is not null ? "ClickSend:Username"    : null,
                request.ClickSendApiKey    is not null ? "ClickSend:ApiKey"      : null,
                request.MailjetApiKey      is not null ? "Mailjet:ApiKey"        : null,
                request.MailjetApiSecret   is not null ? "Mailjet:ApiSecret"     : null,
                request.MailjetSenderEmail is not null ? "Mailjet:SenderEmail"   : null,
            }.Where(f => f is not null) },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(snapshot);
    }

    // ── M14 Microsoft SSO — platform app credentials ───────────────────────────
    // Same secret-handling rails as messaging config: written to appsettings.Local.json
    // (git-ignored, process-only), reloaded live, and surfaced back with the secret
    // masked. The client id is public and returned in full.

    [HttpGet("sso/config")]
    public IActionResult SsoConfig() => Ok(_ssoConfig.GetSnapshot());

    [HttpPost("sso/config")]
    public async Task<IActionResult> UpdateSsoConfig([FromBody] UpdateSsoConfigRequest request)
    {
        if (request is null)
            return BadRequest(new { message = "SSO configuration is required." });

        var snapshot = await _ssoConfig.UpdateAsync(
            request.Enabled,
            request.ClientId,
            request.ClientSecret,
            HttpContext.RequestAborted);

        await _audit.LogAsync(
            GetTenantId(),
            GetUserId(),
            "sso.config.updated",
            "SystemSsoConfig",
            null,
            new
            {
                request.Enabled,
                clientIdSet = request.ClientId is not null,
                // Never log the secret — only that it changed.
                clientSecretChanged = request.ClientSecret is not null,
            },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(snapshot);
    }

    [HttpGet("billing/config")]
    public IActionResult BillingConfig()
    {
        return Ok(_billingConfig.GetSnapshot());
    }

    [HttpPost("billing/config")]
    public async Task<IActionResult> UpdateBillingConfig([FromBody] UpdateBillingConfigRequest request)
    {
        if (request is null)
            return BadRequest(new { message = "Billing configuration is required." });

        BillingConfigSnapshot snapshot;
        try
        {
            snapshot = await _billingConfig.UpdateStripeConfigAsync(
                null, null,
                request.PerDevicePriceId,
                HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }

        await _audit.LogAsync(
            GetTenantId(),
            GetUserId(),
            "billing.config.updated",
            "SystemBillingConfig",
            null,
            new { perDevicePriceId = MaskPriceId(snapshot.PerDevicePriceId) },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(snapshot);
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> Tenants()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Subdomain,
                t.BillingStatus,
                t.LicenseStart,
                t.LicenseEnd,
                t.SuspendedAt,
                t.SuspendedReason,
                t.IsComplimentary,
                t.ComplimentaryReason,
                t.CreatedAt,
            })
            .ToListAsync();

        var deviceCounts = await ActiveDeviceCountsAsync();
        var userCounts = await _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        return Ok(new
        {
            tenants = tenants.Select(t =>
            {
                var deviceCount = deviceCounts.GetValueOrDefault(t.Id);
                return new
                {
                    t.Id,
                    t.Name,
                    t.Subdomain,
                    deviceCount,
                    userCount = userCounts.GetValueOrDefault(t.Id),
                    billingStatus = t.BillingStatus.ToString(),
                    subscriptionStartedAt = t.LicenseStart,
                    subscriptionEndsAt = t.LicenseEnd,
                    monthlyBill = t.IsComplimentary ? 0m : BillingPlanRules.CurrentBill(deviceCount),
                    t.SuspendedAt,
                    t.SuspendedReason,
                    t.IsComplimentary,
                    t.ComplimentaryReason,
                    t.CreatedAt,
                };
            }),
        });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> Tenant(Guid id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        var userRows = await _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.TenantId == id)
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Role,
                u.IsPlatformAdmin,
                mfaEnabled = u.MfaSecret != null,
                u.LastLogin,
                u.CreatedAt,
            })
            .ToListAsync();
        var users = userRows.Select(u => new
        {
            u.Id,
            u.Email,
            role = u.Role.ToString(),
            u.IsPlatformAdmin,
            u.mfaEnabled,
            u.LastLogin,
            u.CreatedAt,
        });

        var deviceStatusCounts = await _db.Devices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.TenantId == id)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var since = DateTime.UtcNow.AddDays(-30);
        var recentNotificationVolume = await _db.Notifications.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.TenantId == id && n.CreatedAt >= since)
            .CountAsync();

        var activeDeviceCount = deviceStatusCounts
            .Where(x => x.Status == DeviceStatus.Active)
            .Sum(x => x.Count);

        return Ok(new
        {
            tenant = new
            {
                tenant.Id,
                tenant.Name,
                tenant.Subdomain,
                billingStatus = tenant.BillingStatus.ToString(),
                tenant.LicenseStart,
                tenant.LicenseEnd,
                tenant.StripeCustomerId,
                tenant.StripeSubscriptionId,
                tenant.SuspendedAt,
                tenant.SuspendedReason,
                tenant.IsComplimentary,
                tenant.ComplimentaryReason,
                activeDeviceCount,
                monthlyBill = tenant.IsComplimentary ? 0m : BillingPlanRules.CurrentBill(activeDeviceCount),
                recentNotificationVolume,
                tenant.CreatedAt,
                tenant.UpdatedAt,
            },
            users,
            deviceStatusCounts = deviceStatusCounts.Select(x => new { status = x.Status.ToString(), x.Count }),
        });
    }

    [HttpGet("billing-overview")]
    public async Task<IActionResult> BillingOverview()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .Select(t => new { t.Id, t.BillingStatus, t.IsComplimentary })
            .ToListAsync();

        var deviceCounts = await ActiveDeviceCountsAsync();
        var totalDevices = deviceCounts.Values.Sum();
        var monthlyRecurringRevenue = tenants
            .Where(t => t.BillingStatus != BillingStatus.Canceled && !t.IsComplimentary)
            .Sum(t => BillingPlanRules.CurrentBill(deviceCounts.GetValueOrDefault(t.Id)));

        var byBillingStatus = tenants
            .GroupBy(t => t.BillingStatus)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .OrderBy(x => x.status)
            .ToList();

        return Ok(new
        {
            totalTenants = tenants.Count,
            totalDevices,
            monthlyRecurringRevenue,
            byBillingStatus,
        });
    }

    // ─── Tenant lifecycle (Platform Admin) ─────────────────────────────────────

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        if (request is null) return BadRequest("Body required.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tenant name is required.");
        if (string.IsNullOrWhiteSpace(request.OwnerEmail))
            return BadRequest("Owner email is required.");

        var email = request.OwnerEmail.Trim().ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
            return Conflict("An account with that email already exists.");

        // Subdomain: explicit (must pass validation) or auto-allocated from name.
        string subdomain;
        if (!string.IsNullOrWhiteSpace(request.Subdomain))
        {
            subdomain = request.Subdomain.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain, @"^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$"))
                return BadRequest("Subdomain must be 1-32 chars, lowercase alphanumeric or hyphen, no leading/trailing hyphen.");
            if (await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == subdomain))
                return Conflict("Subdomain is already in use.");
        }
        else
        {
            subdomain = await AllocateSubdomainAsync(request.Name);
        }

        var now = DateTime.UtcNow;
        var isComp = request.IsComplimentary;
        var trialDays = isComp ? 0 : Math.Clamp(request.TrialDays ?? 0, 0, 3650);

        using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            Subdomain = subdomain,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            EnrollmentKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            BillingStatus = isComp ? BillingStatus.Active
                          : trialDays > 0 ? BillingStatus.Trialing
                          : BillingStatus.Active,
            LicenseStart = now,
            LicenseEnd = isComp ? null
                       : trialDays > 0 ? now.AddDays(trialDays)
                       : null,
            IsComplimentary = isComp,
            ComplimentaryReason = isComp ? Truncate(request.Note?.Trim(), 500) : null,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // If admin supplied an initial password, account is immediately usable.
        // Otherwise create the user in PendingPasswordSet and email the magic-link
        // set-password flow — same path the trial-approval already uses.
        var initialPassword = request.InitialPassword?.Trim();
        var setPasswordViaEmail = string.IsNullOrEmpty(initialPassword);

        var user = new AppUser
        {
            TenantId = tenant.Id,
            FullName = string.IsNullOrWhiteSpace(request.OwnerFullName) ? null : request.OwnerFullName.Trim(),
            Email = email,
            UserName = email,
            PhoneNumber = string.IsNullOrWhiteSpace(request.OwnerPhone) ? null : request.OwnerPhone.Trim(),
            Role = UserRole.SuperAdmin,
            SecurityStamp = Guid.NewGuid().ToString(),
            RegistrationStep = setPasswordViaEmail ? RegistrationStep.PendingPasswordSet : RegistrationStep.Complete,
        };

        var createResult = setPasswordViaEmail
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, initialPassword!);
        if (!createResult.Succeeded)
        {
            await tx.RollbackAsync();
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description).ToArray() });
        }

        foreach (var template in TemplatesController.BuildDefaultTemplates(tenant.Id))
            _db.NotificationTemplates.Add(template);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var emailSent = false;
        if (setPasswordViaEmail)
            emailSent = await SendTrialApprovedEmailAsync(user, tenant);

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.created", "Tenant", tenant.Id.ToString(),
            new
            {
                tenant.Name,
                tenant.Subdomain,
                ownerEmail = email,
                tenant.IsComplimentary,
                trialDays,
                setPasswordViaEmail,
                emailSent,
                request.Note,
            },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new
        {
            tenantId = tenant.Id,
            userId = user.Id,
            tenant.Subdomain,
            setPasswordViaEmail,
            emailSent,
        });
    }

    [HttpPost("tenants/{id:guid}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid id, [FromBody] SuspendTenantRequest? request)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();
        if (tenant.SuspendedAt.HasValue) return Conflict("Tenant is already suspended.");

        tenant.SuspendedAt = DateTime.UtcNow;
        tenant.SuspendedReason = Truncate(request?.Reason?.Trim(), 500);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.suspended", "Tenant", tenant.Id.ToString(),
            new { tenant.Name, tenant.SuspendedReason },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { tenant.Id, tenant.SuspendedAt, tenant.SuspendedReason });
    }

    [HttpPost("tenants/{id:guid}/resume")]
    public async Task<IActionResult> ResumeTenant(Guid id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();
        if (!tenant.SuspendedAt.HasValue) return Conflict("Tenant is not suspended.");

        tenant.SuspendedAt = null;
        tenant.SuspendedReason = null;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.resumed", "Tenant", tenant.Id.ToString(),
            new { tenant.Name },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { tenant.Id, tenant.SuspendedAt });
    }

    [HttpPost("tenants/{id:guid}/extend")]
    public async Task<IActionResult> ExtendTenant(Guid id, [FromBody] ExtendTenantRequest request)
    {
        if (request is null || request.Days <= 0 || request.Days > 3650)
            return BadRequest("Days must be between 1 and 3650.");

        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        var anchor = tenant.LicenseEnd is { } current && current > DateTime.UtcNow
            ? current
            : DateTime.UtcNow;
        tenant.LicenseEnd = anchor.AddDays(request.Days);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.extended", "Tenant", tenant.Id.ToString(),
            new { tenant.Name, request.Days, newLicenseEnd = tenant.LicenseEnd },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { tenant.Id, tenant.LicenseEnd });
    }

    [HttpPost("tenants/{id:guid}/grant-complimentary")]
    public async Task<IActionResult> GrantComplimentary(Guid id, [FromBody] GrantComplimentaryRequest? request)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        tenant.IsComplimentary = true;
        tenant.ComplimentaryReason = Truncate(request?.Reason?.Trim(), 500);
        // Clear LicenseEnd so the tenant never expires.
        tenant.LicenseEnd = null;
        // Promote billing status off Trialing/PastDue so UI surfaces match.
        if (tenant.BillingStatus is BillingStatus.Trialing or BillingStatus.PastDue or BillingStatus.Canceled)
            tenant.BillingStatus = BillingStatus.Active;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.grant_complimentary", "Tenant", tenant.Id.ToString(),
            new { tenant.Name, tenant.ComplimentaryReason },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { tenant.Id, tenant.IsComplimentary, tenant.ComplimentaryReason });
    }

    [HttpPost("tenants/{id:guid}/revoke-complimentary")]
    public async Task<IActionResult> RevokeComplimentary(Guid id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();
        if (!tenant.IsComplimentary) return Conflict("Tenant is not complimentary.");

        tenant.IsComplimentary = false;
        tenant.ComplimentaryReason = null;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.revoke_complimentary", "Tenant", tenant.Id.ToString(),
            new { tenant.Name },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { tenant.Id, tenant.IsComplimentary });
    }

    [HttpDelete("tenants/{id:guid}")]
    public async Task<IActionResult> DeleteTenant(Guid id, [FromQuery] string? confirm = null)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return NotFound();

        // Caller cannot delete the tenant they themselves belong to — prevents a
        // platform admin from accidentally wiping their own home tenant.
        if (tenant.Id == GetTenantId())
            return BadRequest("Refusing to delete the tenant you are signed into.");

        // Type-to-confirm: caller must pass the exact tenant name as ?confirm=.
        if (!string.Equals(confirm, tenant.Name, StringComparison.Ordinal))
            return BadRequest("Confirmation text does not match the tenant name.");

        // Bulk-delete with EF 8 ExecuteDelete — bypasses query filters and skips
        // hydrating thousands of rows into the change tracker. Order matters: kill
        // Restrict-FK rows first (Notifications, Users) so the cascade behavior
        // on the rest fires cleanly when we drop the tenant row.
        await _db.NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.Notification.TenantId == id).ExecuteDeleteAsync();
        await _db.Notifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == id).ExecuteDeleteAsync();
        await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == id).ExecuteDeleteAsync();

        _db.Tenants.Remove(tenant);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.tenant.deleted", "Tenant", id.ToString(),
            new { tenant.Name, tenant.Subdomain },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    // ─── Cross-tenant user ops (Platform Admin) ────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? search = null, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var needle = search?.Trim().ToLowerInvariant();

        var query = _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(needle))
        {
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(needle)) ||
                (u.FullName != null && u.FullName.ToLower().Contains(needle)));
        }

        var rows = await query
            .OrderBy(u => u.Email)
            .Take(limit)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.TenantId,
                tenantName = u.Tenant.Name,
                u.Role,
                u.IsPlatformAdmin,
                mfaEnabled = u.MfaSecret != null,
                u.LastLogin,
                u.CreatedAt,
            })
            .ToListAsync();

        return Ok(new
        {
            users = rows.Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.TenantId,
                u.tenantName,
                role = u.Role.ToString(),
                u.IsPlatformAdmin,
                u.mfaEnabled,
                u.LastLogin,
                u.CreatedAt,
            }),
        });
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> SendUserPasswordReset(Guid id)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(user.Email))
            return BadRequest("User has no email on file.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl = _config["App:BaseUrl"] ?? "https://toastnotification.com";
        var link = $"{baseUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        var emailSent = true;
        try
        {
            await _email.SendAsync(
                user.Email,
                user.FullName ?? user.Email,
                "Reset your password — Toast Notification",
                EmailTemplates.PasswordReset(user.FullName, link));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send platform-initiated password reset email for {UserId}", user.Id);
            emailSent = false;
        }

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.user.password_reset_sent", "AppUser", user.Id.ToString(),
            new { user.Email, user.TenantId, emailSent },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { user.Id, emailSent });
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var callerUserId = GetUserId();
        if (callerUserId == id)
            return BadRequest("Refusing to delete your own account.");

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        // Don't strand a tenant: refuse to remove the last SuperAdmin in a tenant
        // that still has other users. Platform admin can delete the tenant outright
        // if that's the intent.
        if (user.Role == UserRole.SuperAdmin)
        {
            var siblingSuperAdmins = await _db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == user.TenantId && u.Id != user.Id && u.Role == UserRole.SuperAdmin);
            var siblingUsers = await _db.Users.IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == user.TenantId && u.Id != user.Id);
            if (siblingUsers > 0 && siblingSuperAdmins == 0)
                return BadRequest("Cannot remove the last tenant owner while other users remain. Promote another user first.");
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return StatusCode(500, result.Errors.Select(e => e.Description));

        await _audit.LogAsync(
            GetTenantId(), GetUserId(),
            "platform.user.deleted", "AppUser", id.ToString(),
            new { user.Email, user.TenantId, role = user.Role.ToString() },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    [HttpGet("devices")]
    public async Task<IActionResult> Devices([FromQuery] Guid? tenantId = null)
    {
        var query = _db.Devices.IgnoreQueryFilters()
            .Include(d => d.Tenant)
            .AsNoTracking()
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(d => d.TenantId == tenantId.Value);

        var deviceRows = await query
            .OrderByDescending(d => d.LastPing ?? d.RegisteredAt)
            .Take(500)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                tenantName = d.Tenant.Name,
                d.DeviceName,
                d.Username,
                d.OsVersion,
                d.AgentVersion,
                d.Status,
                d.LastPing,
                d.RegisteredAt,
            })
            .ToListAsync();
        var devices = deviceRows.Select(d => new
        {
            d.Id,
            d.TenantId,
            d.tenantName,
            d.DeviceName,
            d.Username,
            d.OsVersion,
            d.AgentVersion,
            status = d.Status.ToString(),
            d.LastPing,
            d.RegisteredAt,
        });

        return Ok(new { devices });
    }

    private async Task<Dictionary<Guid, int>> ActiveDeviceCountsAsync()
    {
        return await _db.Devices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Status == DeviceStatus.Active)
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);
    }

    private async Task<string> AllocateSubdomainAsync(string companyName)
    {
        var root = SlugifyTenantName(companyName);
        if (string.IsNullOrWhiteSpace(root))
            root = $"tenant-{RandomSuffix()}";

        var candidate = root;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == candidate))
                return candidate;

            candidate = $"{root}-{RandomSuffix()}";
        }

        throw new InvalidOperationException("Could not allocate a unique subdomain.");
    }

    private async Task<bool> SendTrialApprovedEmailAsync(AppUser user, Tenant tenant)
    {
        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var baseUrl = _config["App:BaseUrl"] ?? "https://toastnotification.com";
            var encodedToken = Uri.EscapeDataString(token);
            var link = $"{baseUrl}/set-password?userId={user.Id}&token={encodedToken}";

            await _email.SendAsync(
                user.Email!,
                user.FullName ?? user.Email!,
                "Your Toast Notification trial is approved",
                EmailTemplates.TrialApproved(user.FullName ?? user.Email!, tenant.Name, link));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send trial approval email to user {UserId}", user.Id);
            return false;
        }
    }

    private static string SlugifyTenantName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var lower = name.Trim().ToLowerInvariant();
        var chars = new System.Text.StringBuilder(lower.Length);
        var prevHyphen = false;
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                chars.Append(ch);
                prevHyphen = false;
            }
            else if (!prevHyphen && chars.Length > 0)
            {
                chars.Append('-');
                prevHyphen = true;
            }
        }
        return chars.ToString().Trim('-');
    }

    private static string RandomSuffix()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var sb = new System.Text.StringBuilder(4);
        foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }

    private Guid GetTenantId()
    {
        var value = User.FindFirstValue("tenantId");
        return Guid.TryParse(value, out var tenantId) ? tenantId : Guid.Empty;
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string MaskPriceId(string priceId)
    {
        if (priceId.Length <= 12) return "price_***";
        return $"{priceId[..10]}...{priceId[^4..]}";
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public sealed record UpdateBillingConfigRequest(string? PerDevicePriceId);

public sealed record ReviewTrialRequestRequest(string? Note, int? TrialDays);

public sealed record UpdateMessagingConfigRequest(
    string? ClickSendUsername,
    string? ClickSendApiKey,
    string? MailjetApiKey,
    string? MailjetApiSecret,
    string? MailjetSenderEmail);

// Microsoft SSO platform credentials. ClientSecret: null = leave unchanged,
// "__clear__" = remove, anything else = set/rotate (write-only — never returned).
public sealed record UpdateSsoConfigRequest(
    bool? Enabled,
    string? ClientId,
    string? ClientSecret);

public sealed record SuspendTenantRequest(string? Reason);
public sealed record ExtendTenantRequest(int Days);
public sealed record GrantComplimentaryRequest(string? Reason);

public sealed record CreateTenantRequest(
    string Name,
    string? Subdomain,
    string OwnerEmail,
    string? OwnerFullName,
    string? OwnerPhone,
    string? InitialPassword,
    int? TrialDays,
    bool IsComplimentary,
    string? Note);
