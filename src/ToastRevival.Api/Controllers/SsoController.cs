using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// Microsoft Entra SSO — backend-driven OIDC authorization-code flow.
/// GET /start    → 302 to Microsoft's /authorize (sets a sealed state cookie)
/// GET /callback → validates the returned code, gates it to an opted-in tenant,
///                 links to an existing user, and issues our own JWT.
///
/// Anonymous by design (the user has no session yet). Brute-force/abuse is bounded
/// by the same login-per-ip rate limiter used by password login.
/// </summary>
[ApiController]
[Route("api/auth/sso/microsoft")]
public class SsoController : ControllerBase
{
    private const string StateCookieName = "toast_sso_ms";
    private const string ExternalProvider = "microsoft";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly IMicrosoftSsoService _sso;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly IConfiguration _config;
    private readonly ILogger<SsoController> _logger;

    public SsoController(
        IMicrosoftSsoService sso,
        AppDbContext db,
        ITokenService tokens,
        IDataProtectionProvider dataProtection,
        IConfiguration config,
        ILogger<SsoController> logger)
    {
        _sso = sso;
        _db = db;
        _tokens = tokens;
        _protector = dataProtection.CreateProtector("ToastRevival.Sso.Microsoft.v1").ToTimeLimitedDataProtector();
        _config = config;
        _logger = logger;
    }

    /// <summary>Anonymous — lets the login page decide whether to show the
    /// "Sign in with Microsoft" button without exposing any credential.</summary>
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { enabled = _sso.IsEnabled });

    [HttpGet("start")]
    [EnableRateLimiting("login-per-ip")]
    public async Task<IActionResult> Start()
    {
        if (!_sso.IsEnabled) return NotFound();

        var state = RandomToken();
        var nonce = RandomToken();

        // Seal state+nonce into an encrypted, self-expiring cookie. No server-side
        // session store needed; the cookie is tamper-proof (DataProtection) and the
        // values never leave it. SameSite=Lax so it rides the top-level GET redirect
        // back from Microsoft but not cross-site sub-requests.
        var sealed_ = _protector.Protect($"{state}|{nonce}", StateLifetime);
        Response.Cookies.Append(StateCookieName, sealed_, new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,
            SameSite = SameSiteMode.Lax,
            Path     = "/api/auth/sso/microsoft",
            MaxAge   = StateLifetime,
        });

        string authorizeUrl;
        try
        {
            authorizeUrl = await _sso.BuildAuthorizeUrlAsync(state, nonce, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Microsoft authorize URL.");
            return LoginError("unavailable");
        }
        return Redirect(authorizeUrl);
    }

    [HttpGet("callback")]
    [EnableRateLimiting("login-per-ip")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        // State cookie is single-use — clear it no matter how this turns out.
        var cookie = Request.Cookies[StateCookieName];
        Response.Cookies.Delete(StateCookieName, new CookieOptions { Path = "/api/auth/sso/microsoft" });

        if (!_sso.IsEnabled) return LoginError("unavailable");

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogInformation("Microsoft SSO returned error {Error}: {Desc}", error, Truncate(errorDescription, 300));
            return LoginError("denied");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(cookie))
            return LoginError("invalid");

        // CSRF: the state Microsoft echoed must match the one we sealed at /start.
        string expectedState, expectedNonce;
        try
        {
            var parts = _protector.Unprotect(cookie).Split('|', 2);
            expectedState = parts[0];
            expectedNonce = parts.Length > 1 ? parts[1] : string.Empty;
        }
        catch
        {
            // Tampered or expired (older than StateLifetime).
            return LoginError("expired");
        }

        if (!FixedTimeEquals(state, expectedState))
            return LoginError("state");

        MicrosoftIdentity identity;
        try
        {
            identity = await _sso.ExchangeCodeAsync(code, expectedNonce, HttpContext.RequestAborted);
        }
        catch (SsoException ex)
        {
            _logger.LogWarning("Microsoft SSO exchange failed: {Message}", ex.Message);
            return LoginError("failed");
        }

        // ── THE GATE ────────────────────────────────────────────────────────────
        // A valid Microsoft token proves IDENTITY, not AUTHORIZATION. It resolves
        // to a Toast tenant ONLY if that tenant opted in by mapping this exact
        // directory id and enabling SSO. Any other directory — rejected here.
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.SsoEnabled && t.AzureAdTenantId == identity.TenantId);

        if (tenant is null)        return LoginError("not_enabled");
        if (tenant.SuspendedAt != null) return LoginError("suspended");
        if (tenant.SsoRequireMfa && !identity.MfaSatisfied) return LoginError("mfa_required");

        // Link-only: the federated user must already exist in THIS tenant. Match
        // first on the stable Entra object id (bound on a prior sign-in), then fall
        // back to a verified email match within the mapped tenant.
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u =>
            u.TenantId == tenant.Id
            && u.ExternalProvider == ExternalProvider
            && u.ExternalId == identity.ObjectId);

        if (user is null)
        {
            var normalizedEmail = identity.Email.ToUpperInvariant();
            user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u =>
                u.TenantId == tenant.Id && u.NormalizedEmail == normalizedEmail);

            if (user is null)
                return LoginError("no_account");

            // FIX-XT-3 (2026-06-01): this is the FIRST federated sign-in for this user
            // (no oid bound yet) and the match is by email/UPN ONLY. Entra does not
            // assert here that the email is verified, and a directory/Global admin can
            // set a user's UPN/mail to collide with a privileged Toast account — so an
            // unverified-email auto-link into an elevated account is an intra-directory
            // privilege-escalation path. Refuse to auto-link email→elevated; those must
            // be oid-pre-bound by an existing admin before first SSO sign-in. Technician
            // self-link via email is unchanged. (Whether to additionally require an
            // email_verified / xms_edov claim is a directory-dependent policy decision —
            // see REVIEW_LEDGER XT-3, owner: Keith.)
            if (user.Role >= UserRole.Admin || user.IsPlatformAdmin)
                return LoginError("link_requires_admin");

            // First federated sign-in for this user — bind the immutable Entra id
            // so future sign-ins match on it even if the email/UPN later changes.
            // Guard against an oid already claimed by a different user in the tenant.
            var oidTaken = await _db.Users.IgnoreQueryFilters().AnyAsync(u =>
                u.TenantId == tenant.Id
                && u.ExternalProvider == ExternalProvider
                && u.ExternalId == identity.ObjectId
                && u.Id != user.Id);
            if (oidTaken) return LoginError("link_conflict");

            user.ExternalProvider = ExternalProvider;
            user.ExternalId = identity.ObjectId;
        }

        if (user.RegistrationStep != RegistrationStep.Complete)
            return LoginError("incomplete");

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // SSO satisfied strong auth at the IdP — no SMS step. Hand the JWT to the
        // SPA via the URL fragment so it never lands in server logs or the Referer.
        //
        // FIX-MFA-6 (2026-06-01): when Entra asserted MFA (amr contains "mfa"), reflect
        // that into an MFA-elevated token (mfa=true + mfa_at) so the SSO session can
        // satisfy the step-up gates (send a toast, change the lock screen) without a
        // second, redundant prompt for a factor the IdP already enforced. Otherwise mint
        // a plain session token and the user steps up natively when a gate demands it.
        // Without this an Entra-MFA'd SSO admin was fail-closed OUT of the very actions
        // the owner wants gated, with no native way to elevate.
        // REVIEW-2026-06-06 AA-M6 REJECTED-by-design: JWT-in-fragment is current SSO callback mechanism; exchange-code pattern requires persistent server-side token store (Redis or DB session) that the current stateless API does not include; accepted risk documented, planned as SSO-hardening milestone
        var jwt = identity.MfaSatisfied ? _tokens.CreateMfaToken(user) : _tokens.CreateUserToken(user);
        return Redirect($"{FrontendBase()}/sso/callback#token={Uri.EscapeDataString(jwt)}");
    }

    private string FrontendBase() =>
        (_config["App:BaseUrl"] ?? "https://toastnotification.com").TrimEnd('/');

    // Generic, non-enumerating error redirect. The reason is a short opaque code
    // the Login page maps to a friendly message — it never reveals which step
    // failed in a way that helps an attacker probe accounts.
    private RedirectResult LoginError(string reason) =>
        Redirect($"{FrontendBase()}/login?sso_error={Uri.EscapeDataString(reason)}");

    private static string RandomToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
