using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToastRevival.Api.Services;

/// <summary>
/// Stores the Microsoft SSO app credentials (client id + secret + enabled flag)
/// in appsettings.Local.json — the runtime override that stays out of git and is
/// only writable by the process. Mirrors MessagingConfigService exactly so secret
/// handling is consistent across the platform: the secret reaches the box only by
/// a platform admin pasting it into the panel, never through git or a chat log.
/// MicrosoftSsoService reads these keys live from IConfiguration, so a write here
/// (followed by the reload below) takes effect on the next sign-in with no restart.
/// </summary>
public class SsoConfigService : ISsoConfigService
{
    private const string ClearSentinel = "__clear__";
    // ARCH-L1: FileLock and JsonOptions are now shared via LocalSettingsStore.

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SsoConfigService> _logger;

    public SsoConfigService(IConfiguration config, IWebHostEnvironment env, ILogger<SsoConfigService> logger)
    {
        _config = config;
        _env    = env;
        _logger = logger;
    }

    public SsoConfigSnapshot GetSnapshot()
    {
        var secret = (_config["Sso:Microsoft:ClientSecret"] ?? string.Empty).Trim();
        return new SsoConfigSnapshot(
            Enabled:            _config.GetValue<bool>("Sso:Microsoft:Enabled"),
            ClientId:           string.IsNullOrWhiteSpace(_config["Sso:Microsoft:ClientId"]) ? null : _config["Sso:Microsoft:ClientId"]!.Trim(),
            HasClientSecret:    !string.IsNullOrWhiteSpace(secret),
            MaskedClientSecret: string.IsNullOrWhiteSpace(secret) ? null : Mask(secret),
            Authority:          (_config["Sso:Microsoft:Authority"] ?? "https://login.microsoftonline.com/organizations/v2.0").Trim(),
            RedirectUri:        (_config["Sso:Microsoft:RedirectUri"] ?? string.Empty).Trim());
    }

    public Task<SsoConfigSnapshot> UpdateAsync(bool? enabled, string? clientId, string? clientSecret, CancellationToken ct = default)
    {
        lock (LocalSettingsStore.FileLock)
        {
            ct.ThrowIfCancellationRequested();

            var path = Path.Combine(_env.ContentRootPath, "appsettings.Local.json");
            var root = LocalSettingsStore.ReadOrCreateRoot(path);

            if (root["Sso"] is not JsonObject sso)
            {
                sso = new JsonObject();
                root["Sso"] = sso;
            }
            if (sso["Microsoft"] is not JsonObject ms)
            {
                ms = new JsonObject();
                sso["Microsoft"] = ms;
            }

            if (enabled is bool en)
                ms["Enabled"] = en;

            if (clientId is not null)
            {
                var trimmed = clientId.Trim();
                ms["ClientId"] = trimmed.Length == 0 ? null : trimmed;
            }

            // Secret: null/empty = keep existing; sentinel = remove; else set.
            if (clientSecret == ClearSentinel)
                ms["ClientSecret"] = null;
            else if (!string.IsNullOrWhiteSpace(clientSecret))
                ms["ClientSecret"] = clientSecret.Trim();

            LocalSettingsStore.WriteRoot(path, root);

            if (_config is IConfigurationRoot configRoot)
                configRoot.Reload();

            _logger.LogInformation("Microsoft SSO configuration updated via platform admin panel.");
        }

        return Task.FromResult(GetSnapshot());
    }

    private static string Mask(string value)
    {
        if (value.Length <= 6) return "****";
        return value[..3] + "****" + value[^3..];
    }
}
