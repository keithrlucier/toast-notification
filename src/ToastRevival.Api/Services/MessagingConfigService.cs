using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToastRevival.Api.Services;

public class MessagingConfigService : IMessagingConfigService
{
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MessagingConfigService> _logger;

    public MessagingConfigService(
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<MessagingConfigService> logger)
    {
        _config = config;
        _env    = env;
        _logger = logger;
    }

    public MessagingConfigSnapshot GetSnapshot()
    {
        var csUser   = (_config["ClickSend:Username"]      ?? string.Empty).Trim();
        var csKey    = (_config["ClickSend:ApiKey"]        ?? string.Empty).Trim();
        var mjKey    = (_config["Mailjet:ApiKey"]          ?? string.Empty).Trim();
        var mjSecret = (_config["Mailjet:ApiSecret"]       ?? string.Empty).Trim();
        var mjEmail  = (_config["Mailjet:SenderEmail"]     ?? string.Empty).Trim();

        return new MessagingConfigSnapshot(
            HasClickSendUsername  : !string.IsNullOrWhiteSpace(csUser),
            HasClickSendApiKey    : !string.IsNullOrWhiteSpace(csKey),
            HasMailjetApiKey      : !string.IsNullOrWhiteSpace(mjKey),
            HasMailjetApiSecret   : !string.IsNullOrWhiteSpace(mjSecret),
            HasMailjetSenderEmail : !string.IsNullOrWhiteSpace(mjEmail),
            MaskedClickSendUsername : string.IsNullOrWhiteSpace(csUser) ? null : Mask(csUser),
            MaskedClickSendApiKey   : string.IsNullOrWhiteSpace(csKey)  ? null : Mask(csKey),
            MaskedMailjetApiKey     : string.IsNullOrWhiteSpace(mjKey)  ? null : Mask(mjKey),
            MaskedMailjetApiSecret  : string.IsNullOrWhiteSpace(mjSecret) ? null : Mask(mjSecret),
            MailjetSenderEmail      : string.IsNullOrWhiteSpace(mjEmail) ? null : mjEmail);
    }

    public Task<MessagingConfigSnapshot> UpdateAsync(
        string? clickSendUsername,
        string? clickSendApiKey,
        string? mailjetApiKey,
        string? mailjetApiSecret,
        string? mailjetSenderEmail,
        CancellationToken cancellationToken = default)
    {
        lock (FileLock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(_env.ContentRootPath, "appsettings.Local.json");
            var root = ReadOrCreateRoot(path);

            if (root["ClickSend"] is not JsonObject cs)
            {
                cs = new JsonObject();
                root["ClickSend"] = cs;
            }

            if (root["Mailjet"] is not JsonObject mj)
            {
                mj = new JsonObject();
                root["Mailjet"] = mj;
            }

            if (clickSendUsername  is not null) cs["Username"]    = clickSendUsername.Trim();
            if (clickSendApiKey    is not null) cs["ApiKey"]       = clickSendApiKey.Trim();
            if (mailjetApiKey      is not null) mj["ApiKey"]       = mailjetApiKey.Trim();
            if (mailjetApiSecret   is not null) mj["ApiSecret"]    = mailjetApiSecret.Trim();
            if (mailjetSenderEmail is not null) mj["SenderEmail"]  = mailjetSenderEmail.Trim();

            File.WriteAllText(path, root.ToJsonString(JsonOptions) + Environment.NewLine);

            if (_config is IConfigurationRoot configRoot)
                configRoot.Reload();

            _logger.LogInformation("Messaging configuration updated via platform admin panel.");
        }

        return Task.FromResult(GetSnapshot());
    }

    private static JsonObject ReadOrCreateRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("appsettings.Local.json must be a JSON object.");
    }

    private static string Mask(string value)
    {
        if (value.Length <= 6) return "****";
        return value[..3] + "****" + value[^3..];
    }
}
