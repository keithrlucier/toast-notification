using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly MfaService _mfa;

    public AuthController(UserManager<AppUser> userManager, AppDbContext db, ITokenService tokens, MfaService mfa)
    {
        _userManager = userManager;
        _db = db;
        _tokens = tokens;
        _mfa = mfa;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        var subdomain = NormalizeSubdomain(req.Subdomain) ?? SlugifyTenantName(req.TenantName);
        if (string.IsNullOrEmpty(subdomain))
            return BadRequest("Tenant name must contain at least one alphanumeric character.");

        // Tenant Subdomain is unique. If derived from TenantName collides with an
        // existing tenant, append a short random suffix and retry up to a few times.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain)) break;
            if (req.Subdomain is not null) return Conflict("Subdomain already taken.");
            subdomain = SlugifyTenantName(req.TenantName) + "-" + RandomSuffix();
        }
        if (await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Conflict("Could not allocate a unique subdomain. Provide one explicitly.");

        // Wrap in transaction — orphaned Tenant row if user creation fails otherwise
        using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Name = req.TenantName,
            Subdomain = subdomain,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var user = new AppUser
        {
            TenantId = tenant.Id,
            Email = req.Email,
            UserName = req.Email,
            Role = UserRole.SuperAdmin,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });
        }

        // Seed 6 default notification templates for this tenant.
        // INFO-M5-001: if seeding fails the transaction rolls back cleanly.
        try
        {
            foreach (var template in TemplatesController.BuildDefaultTemplates(tenant.Id))
                _db.NotificationTemplates.Add(template);
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Registration succeeded but template initialization failed. Contact support.");
        }

        await tx.CommitAsync();

        var token = _tokens.CreateUserToken(user);
        var refresh = _tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return Ok(new AuthResponse(token, refresh, expiresAt, user.Id, tenant.Id, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    private static string? NormalizeSubdomain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant();
        // Conservative subdomain charset: a-z, 0-9, hyphen. Must start/end alphanumeric.
        var chars = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-') chars.Append(ch);
        }
        var result = chars.ToString().Trim('-');
        return result.Length == 0 ? null : result;
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

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        // Bypass tenant filter — login is tenant-unaware
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized("Invalid credentials.");

        await PromoteSoleTenantOwnerAsync(user);

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _tokens.CreateUserToken(user);
        var refresh = _tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return Ok(new AuthResponse(token, refresh, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    private async Task PromoteSoleTenantOwnerAsync(AppUser user)
    {
        if (user.Role != UserRole.Admin) return;

        var hasSuperAdmin = await _db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == user.TenantId && u.Role == UserRole.SuperAdmin);
        if (hasSuperAdmin) return;

        var adminCount = await _db.Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == user.TenantId
                && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin));

        if (adminCount == 1)
            user.Role = UserRole.SuperAdmin;
    }

    /// <summary>
    /// Generates a TOTP secret for the calling user and returns the base32
    /// secret + an otpauth:// URI suitable for QR code display.
    /// The secret is saved to AppUser.MfaSecret — existing TOTP sessions on
    /// other devices are invalidated. Admin+ only (no Technician self-enrollment).
    /// </summary>
    [HttpPost("mfa/enroll")]
    [Authorize]
    public async Task<ActionResult<MfaEnrollResponse>> MfaEnroll()
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        if (user.Role == UserRole.Technician)
            return Forbid();

        var (secret, qrUri) = _mfa.GenerateEnrollment(user.Email!);
        user.MfaSecret = secret;
        await _db.SaveChangesAsync();

        return Ok(new MfaEnrollResponse(secret, qrUri));
    }

    /// <summary>
    /// Verifies a TOTP code against the calling user's enrolled secret.
    /// Returns a short-lived MFA-elevated JWT (15 min, mfa=true claim).
    /// Required before calling broadcast-to-all or other Super Admin actions.
    /// </summary>
    [HttpPost("mfa/verify")]
    [Authorize]
    public async Task<ActionResult<MfaVerifyResponse>> MfaVerify([FromBody] MfaVerifyRequest req)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(user.MfaSecret))
            return BadRequest("MFA is not enrolled. Call POST /api/auth/mfa/enroll first.");

        // MfaService.Verify mutates user.LastTotpStep on success (SEC-005 /
        // INFO-M3-001 replay guard). Persist the change so the next call
        // sees the new floor and rejects a replayed code.
        if (!_mfa.Verify(user, req.Code))
            return Unauthorized("Invalid or expired TOTP code.");

        await _db.SaveChangesAsync();

        var expiresAt = DateTime.UtcNow.AddMinutes(15);
        var mfaToken  = _tokens.CreateMfaToken(user);

        return Ok(new MfaVerifyResponse(mfaToken, expiresAt));
    }
}
