using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ToastRevival.Api.Services;

/// <summary>
/// Backend-driven Microsoft Entra OIDC authorization-code flow. No cookie auth
/// middleware, no MSAL token cache — this app's only output is its own JWT, so
/// we redeem the code and validate the id_token directly against Microsoft's
/// published JWKS, then hand the result to the sign-in gate in SsoController.
///
/// Registered as a singleton, but every credential is read LIVE from
/// IConfiguration on each call (not cached in the constructor) so a secret set
/// through the platform admin panel — written to appsettings.Local.json and
/// reloaded — takes effect on the next request with no service restart. Only the
/// OIDC metadata (signing keys) is cached, keyed by authority, since that's
/// expensive to fetch and rarely changes.
/// </summary>
public class MicrosoftSsoService : IMicrosoftSsoService
{
    // OIDC discovery + JWKS, cached per authority. ConfigurationManager refreshes
    // the keys on its own schedule (default 24h) and handles rollover.
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> MetadataCache = new();

    private const string Scopes = "openid profile email";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MicrosoftSsoService> _logger;

    public MicrosoftSsoService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<MicrosoftSsoService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    // ── Live config reads — never cached, so panel edits apply without restart ──
    private string Authority =>
        (_config["Sso:Microsoft:Authority"] ?? "https://login.microsoftonline.com/organizations/v2.0").TrimEnd('/');
    private string? ClientId => _config["Sso:Microsoft:ClientId"];
    private string? ClientSecret => _config["Sso:Microsoft:ClientSecret"];
    private string RedirectUri => _config["Sso:Microsoft:RedirectUri"] ?? string.Empty;

    public bool IsEnabled =>
        _config.GetValue<bool>("Sso:Microsoft:Enabled")
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        // Defense-in-depth: the authority is where we fetch the signing keys we
        // validate tokens against. Pin it to a Microsoft login host so a
        // misconfigured/poisoned Authority can't make us trust attacker-minted
        // keys for a token whose issuer claim we'd otherwise accept.
        && IsTrustedMicrosoftAuthority(Authority);

    private static bool IsTrustedMicrosoftAuthority(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var u)
        && u.Scheme == "https"
        && (u.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase)
            // sovereign clouds — US Gov / China / Germany
            || u.Host.Equals("login.microsoftonline.us", StringComparison.OrdinalIgnoreCase)
            || u.Host.Equals("login.partner.microsoftonline.cn", StringComparison.OrdinalIgnoreCase)
            || u.Host.Equals("login.microsoftonline.de", StringComparison.OrdinalIgnoreCase));

    public async Task<string> BuildAuthorizeUrlAsync(string state, string nonce, CancellationToken ct)
    {
        // The authorize endpoint comes from Microsoft's discovery document, NOT
        // from string-building on the authority. The /v2.0 authority path is only
        // the metadata base; the real authorize endpoint is /oauth2/v2.0/authorize.
        var oidc = await GetMetadataAsync(Authority, ct);

        var query = new Dictionary<string, string?>
        {
            ["client_id"]     = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"]  = RedirectUri,
            ["response_mode"] = "query",
            ["scope"]         = Scopes,
            ["state"]         = state,
            ["nonce"]         = nonce,
            // prompt=select_account avoids silently reusing a stale Windows SSO
            // session on shared machines — the user picks which work account.
            ["prompt"]        = "select_account",
        };

        var qs = string.Join("&", query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        return $"{oidc.AuthorizationEndpoint}?{qs}";
    }

    // OIDC discovery for this authority, cached. Carries authorization_endpoint,
    // token_endpoint, issuer, and signing keys — the source of truth for every
    // endpoint URL so we never drift on Microsoft's path layout.
    private static Task<OpenIdConnectConfiguration> GetMetadataAsync(string authority, CancellationToken ct)
    {
        var cm = MetadataCache.GetOrAdd(
            $"{authority}/.well-known/openid-configuration",
            addr => new ConfigurationManager<OpenIdConnectConfiguration>(
                addr, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever()));
        return cm.GetConfigurationAsync(ct);
    }

    public async Task<MicrosoftIdentity> ExchangeCodeAsync(string code, string expectedNonce, CancellationToken ct)
    {
        if (!IsEnabled)
            throw new SsoException("Microsoft sign-in is not configured.");

        var clientId = ClientId!;

        // Pull the discovery document FIRST — it carries the real token_endpoint
        // and the signing keys. We never hand-build endpoint URLs.
        OpenIdConnectConfiguration oidc;
        try
        {
            oidc = await GetMetadataAsync(Authority, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Microsoft OIDC metadata.");
            throw new SsoException("Could not load Microsoft metadata.");
        }

        // 1. Redeem the authorization code at the discovery-published token endpoint.
        var http = _httpFactory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = clientId,
            ["client_secret"] = ClientSecret!,
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = RedirectUri,
            ["scope"]         = Scopes,
        });

        using var resp = await http.PostAsync(oidc.TokenEndpoint, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Body can contain error_description — log it server-side only.
            _logger.LogWarning("Microsoft token endpoint returned {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
            throw new SsoException("Authorization code exchange failed.");
        }

        string? idToken;
        try
        {
            using var doc = JsonDocument.Parse(body);
            idToken = doc.RootElement.TryGetProperty("id_token", out var el) ? el.GetString() : null;
        }
        catch (JsonException)
        {
            throw new SsoException("Token endpoint returned an unparseable response.");
        }

        if (string.IsNullOrWhiteSpace(idToken))
            throw new SsoException("Token response did not include an id_token.");

        // 2. Validate the id_token against Microsoft's published signing keys.
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = oidc.SigningKeys,
            ValidateAudience         = true,
            ValidAudience            = clientId,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromMinutes(5),
            ValidateIssuer           = true,
            IssuerValidator          = ValidateEntraIssuer,
        };

        var handler = new JwtSecurityTokenHandler();
        // Keep short claim names (tid, oid, email, nonce, amr) instead of the
        // legacy long SOAP-style URIs the default inbound map rewrites them to.
        handler.InboundClaimTypeMap.Clear();

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Microsoft id_token validation failed.");
            throw new SsoException("Identity token failed validation.");
        }

        // 3. Nonce binds this token to the /authorize request WE initiated, so a
        // token phished from another flow can't be replayed into ours.
        var nonce = principal.FindFirst("nonce")?.Value;
        if (string.IsNullOrEmpty(nonce) || !FixedTimeEquals(nonce, expectedNonce))
            throw new SsoException("Nonce mismatch.");

        var tid   = principal.FindFirst("tid")?.Value;
        var oid   = principal.FindFirst("oid")?.Value;
        var email = principal.FindFirst("email")?.Value
                    ?? principal.FindFirst("preferred_username")?.Value;
        var name  = principal.FindFirst("name")?.Value;
        var mfa   = principal.FindAll("amr").Any(c => string.Equals(c.Value, "mfa", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(tid) || string.IsNullOrWhiteSpace(oid) || string.IsNullOrWhiteSpace(email))
            throw new SsoException("Identity token is missing required claims (tid/oid/email).");

        // Return the email trimmed but NOT case-folded — the link match compares
        // against AppUser.NormalizedEmail using Identity's own normalizer
        // (UpperInvariant) in SsoController, so pre-lowercasing here would risk a
        // non-identity-stable double transform for non-ASCII locals.
        return new MicrosoftIdentity(
            TenantId:     tid!,
            ObjectId:     oid!,
            Email:        email!.Trim(),
            DisplayName:  string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
            MfaSatisfied: mfa);
    }

    /// <summary>
    /// Multitenant issuer check: the issuer must be the canonical Entra v2 issuer
    /// for the tenant the token itself claims (tid). This proves Microsoft signed
    /// the token for that directory; the directory→Toast-tenant authorization gate
    /// runs separately in SsoController against our own DB.
    /// </summary>
    private static string ValidateEntraIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (token is JwtSecurityToken jwt)
        {
            var tid = jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
            if (!string.IsNullOrEmpty(tid))
            {
                var expected = $"https://login.microsoftonline.com/{tid}/v2.0";
                if (string.Equals(issuer, expected, StringComparison.Ordinal))
                    return issuer;
            }
        }
        throw new SecurityTokenInvalidIssuerException("Issuer does not match the token's tenant directory.");
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
