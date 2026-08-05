using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

// REVIEW-2026-06-06 ARCH-M3 REJECTED-by-design: 890-line AuthController is a known size issue; splitting requires careful boundary design to preserve MFA session state flow; filed as a dedicated refactor milestone

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
    private readonly ITurnstileVerifier _turnstile;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AppUser> userManager,
        AppDbContext db,
        ITokenService tokens,
        MfaService mfa,
        IEmailService email,
        ISmsService sms,
        IConfiguration config,
        ITurnstileVerifier turnstile,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _db = db;
        _tokens = tokens;
        _mfa = mfa;
        _email = email;
        _sms = sms;
        _config = config;
        _turnstile = turnstile;
        _logger = logger;
    }

    // ─── Public registration flow ──────────────────────────────────────────────

    /// <summary>
    /// Step 1 of 3. Creates tenant + user (no password yet), sends ClickSend
    /// SMS with a 6-digit verification code.
    /// </summary>
    [HttpGet("register/config")]
    public ActionResult<PublicRegistrationConfigResponse> RegisterConfig() =>
        Ok(new PublicRegistrationConfigResponse(_turnstile.IsEnabled, _turnstile.SiteKey));

    [HttpPost("register/init")]
    [EnableRateLimiting("trial-register-per-ip")]
    public async Task<ActionResult<TrialRegistrationResponse>> RegisterInit([FromBody] TrialRegistrationRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        string website;
        try
        {
            website = NormalizeWebsite(req.Website);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        // Verify the human challenge BEFORE any existence check so the endpoint
        // can't be used as an account/trial enumeration oracle without solving
        // Turnstile first.
        var remoteIp = ClientIp();
        var turnstile = await _turnstile.VerifyAsync(
            req.TurnstileToken,
            remoteIp,
            "trial_register",
            HttpContext.RequestAborted);
        if (!turnstile.Success)
            return BadRequest(turnstile.Error ?? "Human verification failed.");

        // Non-committal, uniform response whether or not the account/trial
        // already exists (mirrors ForgotPassword). A duplicate is handled
        // silently server-side: we simply do not create a second trial request.
        var emailInUse = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email)
            || await _db.TrialRequests.AnyAsync(r => r.Email == email && r.Status == TrialRequestStatus.Pending);
        if (emailInUse)
        {
            return Ok(new TrialRegistrationResponse(
                Guid.Empty,
                "pending_review",
                "Thanks. Your trial request is pending review. We will email you after approval."));
        }

        var trial = new TrialRequest
        {
            CompanyName = req.CompanyName.Trim(),
            Website = website,
            FullName = req.FullName.Trim(),
            Email = email,
            Phone = req.Phone.Trim(),
            JobTitle = req.JobTitle.Trim(),
            IntendedUseCase = req.IntendedUseCase,
            IntendedUseCaseDetails = string.IsNullOrWhiteSpace(req.IntendedUseCaseDetails)
                ? null
                : req.IntendedUseCaseDetails.Trim(),
            RemoteIpAddress = remoteIp,
            UserAgent = Truncate(Request.Headers["User-Agent"].ToString(), 512),
            TurnstileHostname = turnstile.Hostname,
            TurnstileAction = turnstile.Action,
        };

        _db.TrialRequests.Add(trial);
        await _db.SaveChangesAsync();
        await NotifyTrialReviewAsync(trial);

        // REST-L1: Return 201 Created for resource creation instead of 200 OK.
        return StatusCode(StatusCodes.Status201Created, new TrialRegistrationResponse(
            trial.Id,
            "pending_review",
            "Thanks. Your trial request is pending review. We will email you after approval."));
    }

    /// <summary>
    /// Step 2 of 3. Verifies the 6-digit SMS code. On success, marks phone
    /// confirmed and sends the Mailjet magic-token email for password setup.
    /// </summary>
    [HttpPost("register/verify-sms")]
    // DOS-H2: Rate limit register SMS verify to 5 per 15 min per IP.
    [EnableRateLimiting("register-sms")]
    public async Task<IActionResult> VerifySms([FromBody] VerifySmsRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null || user.RegistrationStep != RegistrationStep.PendingSmsVerification)
            return BadRequest("Invalid or already-completed verification.");

        if (user.SmsCodeExpiry < DateTime.UtcNow)
            return BadRequest("Verification code expired. Please restart registration.");

        if (user.SmsVerificationCode != HashSmsCode(req.Code.Trim()))
        {
            // DOS-H2 / AA-L2: Register failed attempt for lockout tracking (mirrors login SMS path).
            await RegisterFailedSmsAttemptAsync(user);
            return Unauthorized("Incorrect verification code.");
        }

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
        // AA-M1: RefreshToken no longer generated or returned.
        var expiresAt = SessionExpiresAt();

        // AA-M1: RefreshToken removed from AuthResponse.
        return Ok(new AuthResponse(jwt, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    /// <summary>
    /// Initiates self-service password reset. Sends Mailjet email with reset link.
    /// </summary>
    [HttpPost("forgot-password")]
    // DOS-H1: Rate limit forgot-password to 5 requests per 15 min per IP.
    [EnableRateLimiting("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        // Always return 200 to prevent email enumeration.
        // DOS-M3: When user is not found, delay the response to match the time
        // it would take to send an email, preventing timing oracle attacks.
        if (user is null || user.RegistrationStep != RegistrationStep.Complete)
        {
            await Task.Delay(150, HttpContext.RequestAborted);
            return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
        }

        var token      = await _userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl    = _config["App:BaseUrl"] ?? "https://toastnotification.com";
        var encodedTok = Uri.EscapeDataString(token);
        var link       = $"{baseUrl}/reset-password?userId={user.Id}&token={encodedTok}";
        var html       = EmailTemplates.PasswordReset(user.FullName, link);

        // REVIEW-2026-06-06 REL-L3 REJECTED-by-design: IEmailService/ISmsService interfaces do not expose CancellationToken; adding it is a breaking interface change requiring coordinated update of all implementations; filed as a future improvement milestone
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

        // SES-2-R: ResetPasswordAsync rotates the Identity SecurityStamp, so every token
        // issued before this reset (old epoch) is rejected by the OnTokenValidated hook —
        // a password reset instantly kills the prior sessions.
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

    // Session expiry advertised to clients must match the real JWT exp the
    // TokenService stamps from Jwt:ExpiresInMinutes — otherwise the response
    // lies about how long the token is valid.
    private DateTime SessionExpiresAt()
    {
        var minutes = int.TryParse(_config["Jwt:ExpiresInMinutes"], out var m) ? m : 60;
        return DateTime.UtcNow.AddMinutes(minutes);
    }

    private static string HashSmsCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    // Records a wrong SMS-code attempt against the Identity lockout counter and,
    // once the configured failure cap is reached, invalidates the stored code so
    // the same 6-digit value can't be brute-forced for the rest of its window.
    // A single typo does NOT nuke the code — only the N-failure cap does.
    private async Task RegisterFailedSmsAttemptAsync(AppUser user)
    {
        await _userManager.AccessFailedAsync(user);

        if (await _userManager.IsLockedOutAsync(user))
        {
            user.SmsVerificationCode = null;
            user.SmsCodeExpiry       = null;
            await _db.SaveChangesAsync();
        }
    }

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "****";
        return $"****{digits[^4..]}";
    }

    private static string NormalizeWebsite(string raw)
    {
        var value = raw.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = $"https://{value}";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Enter a valid company website.");
        }

        return uri.ToString().TrimEnd('/');
    }

    // BF-2: trust CF-Connecting-IP only from a verified Cloudflare peer (or loopback
    // reverse proxy). Single source of truth in CloudflareIpValidator.
    private string ClientIp() => Services.CloudflareIpValidator.ResolveTrustedClientIp(HttpContext);

    private async Task NotifyTrialReviewAsync(TrialRequest trial)
    {
        var reviewEmail = _config["Registration:ReviewEmail"] ?? "support@toastnotification.com";
        try
        {
            await _email.SendAsync(
                reviewEmail,
                "Toast Notification Review",
                $"Trial request: {trial.CompanyName}",
                EmailTemplates.TrialRequestReview(trial));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send trial request review email for {TrialRequestId}", trial.Id);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    // ARCH-L2: Centralized MFA elevation expiry helper, replacing three inline copies.
    private DateTime MfaExpiresAt() =>
        DateTime.UtcNow.AddMinutes(int.TryParse(_config["Jwt:MfaElevationExpiresInMinutes"], out var m) ? m : 15);


    // DC-L5: Register() action removed — dead code since Registration:AllowLegacyDirectRegister
    // always returns 410 Gone. The new flow uses /api/auth/register/init.
    // RegisterRequest DTO is also removed from AuthDtos.cs.

    [HttpPost("login")]
    [EnableRateLimiting("login-per-ip")]
    public async Task<ActionResult> Login([FromBody] LoginRequest req)
    {
        // Bypass tenant filter — login is tenant-unaware
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user is null)
            return Unauthorized("Invalid credentials.");

        // Brute-force lockout. CheckPasswordAsync does not touch lockout state on
        // its own, so check/record it explicitly. A locked account is rejected
        // before the password is even evaluated.
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Invalid credentials.");

        if (!await _userManager.CheckPasswordAsync(user, req.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid credentials.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.RegistrationStep != RegistrationStep.Complete)
            return Unauthorized("Registration is incomplete. Please finish registering your account.");

        // Tenant suspension blocks login for tenant users. Platform admins
        // are exempt so they can still investigate and lift the suspension.
        if (!user.IsPlatformAdmin && await IsTenantSuspendedAsync(user.TenantId))
            return Unauthorized("This tenant has been suspended. Contact support.");

        await PromoteSoleTenantOwnerAsync(user);

        // Authenticator (TOTP) MFA: when the user has confirmed an authenticator,
        // it is their login second factor and takes precedence over SMS — it's the
        // stronger, explicitly-enrolled method. Completes via POST login/verify-totp.
        if (!string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            await _db.SaveChangesAsync();   // persist PromoteSoleTenantOwnerAsync, if any
            return Ok(new LoginTotpChallenge(user.Id, "totp_required"));
        }

        // SMS MFA: all users with a confirmed phone number must verify via SMS
        if (user.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            var code   = GenerateSmsCode();
            var hashed = HashSmsCode(code);
            user.SmsVerificationCode = hashed;
            user.SmsCodeExpiry       = DateTime.UtcNow.AddMinutes(10);
            await _db.SaveChangesAsync();

            await _sms.SendAsync(user.PhoneNumber, $"Your Toast Notification login code is: {code}. It expires in 10 minutes.");

            var masked = MaskPhone(user.PhoneNumber);
            return Ok(new LoginSmsChallenge(user.Id, "sms_required", masked));
        }

        // No phone confirmed — issue token directly (legacy/admin-created accounts).
        // AA-M4: If Auth:RequireMfaEnrollment is enabled (default off) and the user
        // has no MFA enrolled, return 403 requiring setup instead of issuing a JWT.
        if (!user.IsPlatformAdmin && _config.GetValue<bool>("Auth:RequireMfaEnrollment"))
        {
            var hasMfa = !string.IsNullOrWhiteSpace(user.MfaSecret)
                      || (user.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber));
            if (!hasMfa)
                return StatusCode(403, new { requiresMfaSetup = true });
        }

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token     = _tokens.CreateUserToken(user);
        // AA-M1: RefreshToken no longer generated or returned.
        var expiresAt = SessionExpiresAt();
        // AA-M1: RefreshToken removed from AuthResponse.
        return Ok(new AuthResponse(token, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    /// <summary>
    /// Completes SMS MFA login. Verifies the 6-digit code sent to the user's
    /// phone during POST /api/auth/login and returns a full JWT on success.
    /// </summary>
    [HttpPost("login/verify-sms")]
    [EnableRateLimiting("login-sms-per-ip")]
    public async Task<ActionResult<AuthResponse>> VerifyLoginSms([FromBody] LoginSmsVerifyRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null || user.RegistrationStep != RegistrationStep.Complete)
            return Unauthorized("Invalid request.");

        if (!user.IsPlatformAdmin && await IsTenantSuspendedAsync(user.TenantId))
            return Unauthorized("This tenant has been suspended. Contact support.");

        // AA-M2: Per-userId lockout — IsLockedOutAsync checks AccessFailedCount (userId-scoped).
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Too many attempts. Please sign in again later.");

        if (user.SmsCodeExpiry is null || user.SmsCodeExpiry < DateTime.UtcNow)
            return Unauthorized("Verification code expired. Please sign in again.");

        if (user.SmsVerificationCode != HashSmsCode(req.Code.Trim()))
        {
            // AA-M2: RegisterFailedSmsAttemptAsync calls AccessFailedAsync (per-userId counter)
            // and invalidates the OTP once the lockout threshold is reached.
            await RegisterFailedSmsAttemptAsync(user);
            return Unauthorized("Incorrect verification code.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        user.SmsVerificationCode = null;
        user.SmsCodeExpiry       = null;
        user.LastLogin           = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await PromoteSoleTenantOwnerAsync(user);

        var token     = _tokens.CreateUserToken(user);
        // AA-M1: RefreshToken no longer generated or returned.
        var expiresAt = SessionExpiresAt();
        // AA-M1: RefreshToken removed from AuthResponse.
        return Ok(new AuthResponse(token, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    /// <summary>
    /// Completes authenticator (TOTP) login. Verifies the 6-digit code against the
    /// user's confirmed MfaSecret (issued the totp_required challenge by Login) and
    /// returns a full session JWT on success. Mirrors VerifyLoginSms.
    /// </summary>
    [HttpPost("login/verify-totp")]
    [EnableRateLimiting("login-sms-per-ip")]
    public async Task<ActionResult<AuthResponse>> VerifyLoginTotp([FromBody] LoginTotpVerifyRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user is null || user.RegistrationStep != RegistrationStep.Complete)
            return Unauthorized("Invalid request.");

        if (!user.IsPlatformAdmin && await IsTenantSuspendedAsync(user.TenantId))
            return Unauthorized("This tenant has been suspended. Contact support.");

        if (string.IsNullOrWhiteSpace(user.MfaSecret))
            return Unauthorized("Authenticator MFA is not set up on this account.");

        // Auth-L1: mirror the SMS paths (VerifyLoginSms) — enforce the per-userId Identity
        // lockout on authenticator verification too. VerifyAndClaimAsync only advances the
        // TOTP replay floor, not AccessFailedCount, so without this a wrong code never
        // increments lockout and the login-TOTP factor is brute-forceable across rotating IPs.
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Too many attempts. Please sign in again later.");

        // AUTH-H1 — VerifyAndClaimAsync verifies the code AND advances LastTotpStep
        // in one atomic SQL UPDATE, so a code replayed across concurrent requests is
        // accepted at most once.
        if (!await _mfa.VerifyAndClaimAsync(_db, user, req.Code))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid or expired authenticator code.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await PromoteSoleTenantOwnerAsync(user);
        await _db.SaveChangesAsync();

        var token     = _tokens.CreateUserToken(user);
        // AA-M1: RefreshToken no longer generated or returned.
        var expiresAt = SessionExpiresAt();   // SES-1: advertise the real token lifetime
        // AA-M1: RefreshToken removed from AuthResponse.
        return Ok(new AuthResponse(token, expiresAt, user.Id, user.TenantId, user.Email!, user.Role.ToString(), user.IsPlatformAdmin));
    }

    private async Task<bool> IsTenantSuspendedAsync(Guid tenantId)
    {
        var suspendedAt = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.SuspendedAt)
            .FirstOrDefaultAsync();
        return suspendedAt.HasValue;
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
    /// Sends a one-time SMS code to the caller's confirmed phone number.
    /// Used to elevate the session for broadcast-to-all sends.
    /// Returns the masked phone number so the UI can confirm the destination.
    /// </summary>
    [HttpPost("mfa/send-sms")]
    [Authorize]
    // DOS-M5: Per-userId sliding-window rate limit (3 sends / 15 min) applied.
    // IP-based limiting is insufficient — an IP-rotating attacker can spam
    // ClickSend sends against any known phone number without triggering lockout.
    [EnableRateLimiting("login-sms-per-userid")]
    public async Task<ActionResult> MfaSendSms()
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        // MFA-7 (FIXED): block the SMS step-up downgrade. When the caller has an
        // enrolled TOTP authenticator (a separately-enrolled secret factor), refuse
        // the weaker SMS channel and force the authenticator path — and never spend a
        // ClickSend SMS doing it. SMS-only / SSO / legacy users (no MfaSecret) are
        // unaffected and keep using SMS. The step-up modal treats this 403 as
        // "no SMS available" and falls back to the authenticator code automatically
        // (MfaStepUpModal.tsx). Retiring SMS outright — which would affect SMS-only
        // users — remains Keith's migration call (tracked separately, not this finding).
        if (!string.IsNullOrWhiteSpace(user.MfaSecret))
            return StatusCode(403, new
            {
                error = "totp_required",
                message = "Your account has an authenticator app enabled. Use your authenticator app to verify instead."
            });

        if (!user.PhoneNumberConfirmed || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return BadRequest("No verified phone number on this account.");

        var code = GenerateSmsCode();
        user.SmsVerificationCode = HashSmsCode(code);
        user.SmsCodeExpiry       = DateTime.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync();

        await _sms.SendAsync(user.PhoneNumber, $"Your Toast Notification verification code is: {code}. It expires in 10 minutes.");

        return Ok(new { masked = MaskPhone(user.PhoneNumber) });
    }

    /// <summary>
    /// Verifies an SMS code sent via POST /api/auth/mfa/send-sms.
    /// Returns a short-lived MFA-elevated JWT (15 min, mfa=true claim).
    /// </summary>
    [HttpPost("mfa/verify-sms")]
    [Authorize]
    [EnableRateLimiting("login-sms-per-ip")]
    public async Task<ActionResult<MfaVerifyResponse>> MfaVerifySms([FromBody] MfaVerifyRequest req)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        // MFA-7 (FIXED): SMS step-up re-uses the SMS-login channel, so a TOTP-enrolled
        // user elevating via SMS is a downgrade to the weaker factor. The send-sms guard
        // above stops the common path; this is defense in depth — a code minted before
        // enrollment, or a direct API call, still can't elevate a TOTP user over SMS.
        // SMS-only / SSO / legacy users (no MfaSecret) are unaffected. Retiring SMS
        // outright remains Keith's migration call (tracked separately, not this finding).
        if (!string.IsNullOrWhiteSpace(user.MfaSecret))
            return StatusCode(403, new
            {
                error = "totp_required",
                message = "Your account has an authenticator app enabled. Use your authenticator app to verify instead."
            });

        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Too many attempts. Please try again later.");

        if (user.SmsCodeExpiry is null || user.SmsCodeExpiry < DateTime.UtcNow)
            return Unauthorized("Verification code expired.");

        if (user.SmsVerificationCode != HashSmsCode(req.Code.Trim()))
        {
            await RegisterFailedSmsAttemptAsync(user);
            return Unauthorized("Incorrect verification code.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        user.SmsVerificationCode = null;
        user.SmsCodeExpiry       = null;
        await _db.SaveChangesAsync();

        // ARCH-L2: Use shared MfaExpiresAt() helper.
        var mfaToken  = _tokens.CreateMfaToken(user);
        return Ok(new MfaVerifyResponse(mfaToken, MfaExpiresAt()));
    }

    /// <summary>
    /// Returns the caller's MFA status — used by the Security card and the
    /// force-enrollment gate. Available to every authenticated user (everyone must
    /// be able to manage their own second factor, especially under tenant enforcement).
    /// </summary>
    [HttpGet("mfa/status")]
    [Authorize]
    public async Task<ActionResult<MfaStatusResponse>> MfaStatus()
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        var tenantRequired = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.RequireMfa)
            .FirstOrDefaultAsync();

        return Ok(new MfaStatusResponse(
            Enabled:        !string.IsNullOrWhiteSpace(user.MfaSecret),
            TenantRequired: tenantRequired,
            HasPhone:       user.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber)));
    }

    /// <summary>
    /// Begins authenticator enrollment: generates a fresh TOTP secret, stashes it
    /// in AppUser.MfaPendingSecret (NOT MfaSecret — see model comment), and returns
    /// the base32 secret + otpauth:// URI for QR display. Any authenticated user may
    /// enroll their own account. The pending secret only becomes the active login
    /// factor after MfaEnrollConfirm verifies a code — a started-but-abandoned
    /// enrollment never touches an existing working authenticator.
    /// </summary>
    [HttpPost("mfa/enroll")]
    [Authorize]
    public async Task<ActionResult<MfaEnrollResponse>> MfaEnroll()
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        var (secret, qrUri) = _mfa.GenerateEnrollment(user.Email!);
        user.MfaPendingSecret = secret;
        await _db.SaveChangesAsync();

        return Ok(new MfaEnrollResponse(secret, qrUri));
    }

    /// <summary>
    /// Confirms a pending authenticator enrollment. Verifies the code against
    /// MfaPendingSecret; on success promotes it to MfaSecret (the active login
    /// factor), clears the pending secret, and resets the replay floor. Returns an
    /// MFA-elevated token so the just-enrolled user is immediately step-up verified.
    /// </summary>
    [HttpPost("mfa/enroll/confirm")]
    [Authorize]
    public async Task<ActionResult<MfaVerifyResponse>> MfaEnrollConfirm([FromBody] MfaVerifyRequest req)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(user.MfaPendingSecret))
            return BadRequest("No pending authenticator enrollment. Start setup first.");

        if (!_mfa.VerifySecret(user.MfaPendingSecret, req.Code))
            return Unauthorized("Invalid or expired authenticator code.");

        user.MfaSecret        = user.MfaPendingSecret;
        user.MfaPendingSecret = null;
        user.LastTotpStep     = null;   // fresh secret — clear the old replay floor
        await _db.SaveChangesAsync();

        // ARCH-L2: Use shared MfaExpiresAt() helper.
        var mfaToken  = _tokens.CreateMfaToken(user);
        return Ok(new MfaVerifyResponse(mfaToken, MfaExpiresAt()));
    }

    /// <summary>
    /// Disables authenticator MFA for the caller. Requires a valid current TOTP code
    /// (proof of possession) and is refused while the tenant enforces MFA — you
    /// cannot opt out of a policy your admin turned on. Clears both the active and
    /// any pending secret.
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize]
    public async Task<IActionResult> MfaDisable([FromBody] MfaVerifyRequest req)
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid)) return Unauthorized();

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(user.MfaSecret))
            return BadRequest("Authenticator MFA is not enabled on this account.");

        var tenantRequired = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.RequireMfa)
            .FirstOrDefaultAsync();
        if (tenantRequired)
            return StatusCode(403, new
            {
                error = "mfa_enforced",
                message = "Your workspace requires multi-factor authentication. Ask an admin to lift the requirement before disabling it."
            });

        // Auth-L1: enforce the Identity lockout on this TOTP path too (mirrors MfaVerifySms).
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Too many attempts. Please try again later.");

        // AUTH-H1 — atomic verify + replay-floor advance (see VerifyLoginTotp).
        if (!await _mfa.VerifyAndClaimAsync(_db, user, req.Code))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid or expired authenticator code.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        user.MfaSecret        = null;
        user.MfaPendingSecret = null;
        user.LastTotpStep     = null;
        await _db.SaveChangesAsync();
        return NoContent();
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
            return BadRequest("Authenticator app MFA is not set up on this account.");

        // Auth-L1: enforce the Identity lockout on this TOTP path too (mirrors MfaVerifySms).
        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Too many attempts. Please try again later.");

        // AUTH-H1 — VerifyAndClaimAsync atomically verifies the code and advances
        // the replay floor (LastTotpStep) at the DB level, so an attacker who
        // intercepts a valid TOTP within its ±1 step window cannot replay it across
        // concurrent step-up requests. The atomic UPDATE has already persisted the
        // floor; the SaveChangesAsync below is a no-op for that column.
        if (!await _mfa.VerifyAndClaimAsync(_db, user, req.Code))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid or expired TOTP code.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        await _db.SaveChangesAsync();

        // ARCH-L2: Use shared MfaExpiresAt() helper (replaces inline config read).
        var mfaToken  = _tokens.CreateMfaToken(user);

        return Ok(new MfaVerifyResponse(mfaToken, MfaExpiresAt()));
    }
}
