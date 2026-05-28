namespace ToastRevival.Api.Services;

/// <summary>
/// A validated identity asserted by Microsoft Entra after a successful OIDC
/// authorization-code exchange. Carries only what the sign-in gate needs.
///   TenantId:     the Entra Directory (tenant) GUID — the "tid" claim. This is
///                 the value matched against Tenant.AzureAdTenantId.
///   ObjectId:     the stable per-user object id — the "oid" claim. Immutable,
///                 unlike email/UPN, so it's the durable link key.
///   Email:        trimmed email/UPN, matched (case-insensitively, via Identity's
///                 NormalizedEmail) against an existing user for the link-only flow.
///   MfaSatisfied: true when the id_token's "amr" claim asserts an MFA method —
///                 used by the per-tenant SsoRequireMfa gate.
/// </summary>
public record MicrosoftIdentity(string TenantId, string ObjectId, string Email, string? DisplayName, bool MfaSatisfied);

/// <summary>
/// Thrown for any failure during the Microsoft sign-in exchange/validation.
/// The message is for logs only — callers map every failure to a generic,
/// non-enumerating redirect so we never tell an attacker which step failed.
/// </summary>
public class SsoException : Exception
{
    public SsoException(string message) : base(message) { }
}

public interface IMicrosoftSsoService
{
    /// <summary>True only when both the client id AND the env-supplied client
    /// secret are present and the feature is enabled in config.</summary>
    bool IsEnabled { get; }

    /// <summary>Builds the Entra /authorize URL for the front-channel redirect.
    /// state = CSRF token echoed back to /callback; nonce binds the resulting
    /// id_token to this specific authorize request.</summary>
    string BuildAuthorizeUrl(string state, string nonce);

    /// <summary>Redeems the authorization code for an id_token and validates it
    /// (signature against Microsoft's published JWKS, audience, lifetime, issuer
    /// derived from the token's own tid, and nonce). Throws <see cref="SsoException"/>
    /// on any failure.</summary>
    Task<MicrosoftIdentity> ExchangeCodeAsync(string code, string expectedNonce, CancellationToken ct);
}
