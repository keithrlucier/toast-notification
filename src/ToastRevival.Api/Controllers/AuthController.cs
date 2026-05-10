using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IEmailService _email;
    private readonly ISmsService _sms;
    private readonly IConfiguration _config;

    public AuthController(
        UserManager<AppUser> userManager,
        AppDbContext db,
        ITokenService tokens,
        MfaService mfa,
        IEmailService email,
        ISmsService sms,
        IConfiguration config)
    {
        _userManager = userManager;
        _db = db;
        _tokens = tokens;
        _mfa = mfa;
        _email = email;
        _sms = sms;
        _config = config;
    }

    // ─── New M9.A registration flow ────────────────────────────────────────────

    /// <summary>
    /// Step 1 of 3. Creates tenant + user (no password yet), sends ClickSend
    /// SMS with a 6-digit verification code.
    /// </summary>
    [HttpPost("register/init")]
    public async Task<ActionResult<RegisterInitResponse>> RegisterInit([FromBody] RegisterInitRequest req)
    {
        var subdomain = NormalizeSubdomain(req.Subdomain) ?? SlugifyTenantName(req.TenantName);
        if (string.IsNullOrEmpty(subdomain))
            return BadRequest("Tenant name must contain at least one alphanumeric character.");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain)) break;
            if (req.Subdomain is not null) return Conflict("Subdomain already taken.");
            subdomain = SlugifyTenantName(req.TenantName) + "-" + RandomSuffix();
        }
        if (await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Conflict("Could not allocate a unique subdomain.");

        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == req.Email))
            return Conflict("An account with that email already exists.");

        using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Name       = req.TenantName,
            Subdomain  = subdomain,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var code       = GenerateSmsCode();
        var codeHash   = HashSmsCode(code);
        var codeExpiry = DateTime.UtcNow.AddMinutes(10);

        var user = new AppUser
        {
            TenantId             = tenant.Id,
            FullName             = req.FullName.Trim(),
            Email                = req.Email,
            UserName             = req.Email,
            PhoneNumber          = req.Mobile,
            Role                 = UserRole.SuperAdmin,
            SecurityStamp        = Guid.NewGuid().ToString(),
            SmsVerificationCode  = codeHash,
            SmsCodeExpiry        = codeExpiry,
            RegistrationStep     = RegistrationStep.PendingSmsVerification,
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });
        }

        try
        {
            foreach (var template in TemplatesController.BuildDefaultTemplates(tenant.Id))
                _db.NotificationTemplates.Add(template);
            await _db.SaveChangesAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Registration failed during template initialization.");
        }

        await tx.CommitAsync();

        await _sms.SendAsync(req.Mobile, $"Your Toast Notification verification code is: {code}. It expires in 10 minutes.");

        return Ok(new RegisterInitResponse(user.Id, "sms_pending"));
    }

    /// <summary>
    /// Step 2 of 3. Verifies the 6-digit SMS code. On success, marks phone
    /// confirmed and sends the Mailjet magic-token email for password setup.
    /// </summary>
    [HttpPost("register/verify-sms")]
    public async Task<IActionResult> VerifySms([FromBody] VerifySmsRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null || user.RegistrationStep != RegistrationStep.PendingSmsVerification)
            return BadRequest("Invalid or already-completed verification.");

        if (user.SmsCodeExpiry < DateTime.UtcNow)
            return BadRequest("Verification code expired. Please restart registration.");

        if (user.SmsVerificationCode != HashSmsCode(req.Code.Trim()))
            return Unauthorized("Incorrect verification code.");

        user.PhoneNumberConfirmed  = true;
        user.SmsVerificationCode   = null;
        user.SmsCodeExpiry         = null;
        user.RegistrationStep      = RegistrationStep.PendingPasswordSet;
        await _db.SaveChangesAsync();

        var token      = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var baseUrl    = _config["App:BaseUrl"] ?? "https://toastnotification.com";
        var encodedTok = Uri.EscapeDataString(token);
        var link       = $"{baseUrl}/set-password?userId={user.Id}&token={encodedTok}";
        var html       = EmailTemplates.SetPassword(user.FullName ?? user.Email!, link);

        await _email.SendAsync(user.Email!, user.FullName ?? user.Email!, "Set your password — Toast Notification", html);

        return Ok(new { step = "email_sent" });
    }

    /// <summary>
    /// Step 3 of 3. Confirms email token, sets password, returns JWT.
    /// </summary>
    [HttpPost("register/set-password")]
    public async Task<ActionResult<AuthResponse>> SetPassword([FromBody] SetPasswordRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null || user.RegistrationStep != RegistrationStep.PendingPasswordSet)
            return BadRequest("Invalid request or registration step.");

        var confirmResult = await _userManager.ConfirmEmailAsync(user, req.Token);
        if (!confirmResult.Succeeded)
            return BadRequest("Link is invalid or has expired. Please contact support.");

        var addPwResult = await _userManager.AddPasswordAsync(user, req.Password);
        if (!addPwResult.Succeeded)
            return BadRequest(new { errors = addPwResult.Errors.Select(e => e.Description).ToArray() });

        user.RegistrationStep = RegistrationStep.Complete;
        user.LastLogin        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var jwt       = _tokens.CreateUserToken(user);
        var refresh   = _tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return Ok(new AuthResponse(jwt, refresh, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    /// <summary>
    /// Initiates self-service password reset. Sends Mailjet email with reset link.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        // Always return 200 to prevent email enumeration
        if (user is null || user.RegistrationStep != RegistrationStep.Complete)
            return Ok(new { message = "If an account exists for that email, a reset link has been sent." });

        var token      = await _userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl    = _config["App:BaseUrl"] ?? "https://toastnotification.com";
        var encodedTok = Uri.EscapeDataString(token);
        var link       = $"{baseUrl}/reset-password?userId={user.Id}&token={encodedTok}";
        var html       = EmailTemplates.PasswordReset(user.FullName, link);

        await _email.SendAsync(user.Email!, user.FullName ?? user.Email!, "Reset your password — Toast Notification", html);

        return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
    }

    /// <summary>
    /// Completes password reset via token from email.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null)
            return BadRequest("Invalid reset link.");

        var result = await _userManager.ResetPasswordAsync(user, req.Token, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });

        return Ok(new { message = "Password updated. You can now sign in." });
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static string GenerateSmsCode()
    {
        // Cryptographically random 6-digit code, zero-padded
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var n = (int)(BitConverter.ToUInt32(bytes) % 1_000_000);
        return n.ToString("D6");
    }

    private static string HashSmsCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));



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
