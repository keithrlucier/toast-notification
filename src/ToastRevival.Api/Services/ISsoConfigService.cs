namespace ToastRevival.Api.Services;

/// <summary>
/// Platform-admin view of the Microsoft SSO app credentials. The client id is a
/// public identifier so it's returned in full; the client secret is never
/// returned — only whether one is set and a masked preview.
/// </summary>
public record SsoConfigSnapshot(
    bool Enabled,
    string? ClientId,
    bool HasClientSecret,
    string? MaskedClientSecret,
    // Read-only references the panel shows so an admin can build the Entra
    // admin-consent link and confirm the redirect URI matches the app reg.
    string Authority,
    string RedirectUri);

public interface ISsoConfigService
{
    SsoConfigSnapshot GetSnapshot();

    /// <summary>
    /// Persists Microsoft SSO credentials to appsettings.Local.json (the same
    /// git-ignored, chmod-protected runtime override MessagingConfigService uses)
    /// and reloads configuration so the change is live without a restart.
    ///   clientSecret: null/empty = leave unchanged; "__clear__" = remove;
    ///                 anything else = set/rotate.
    /// </summary>
    Task<SsoConfigSnapshot> UpdateAsync(bool? enabled, string? clientId, string? clientSecret, CancellationToken ct = default);
}
