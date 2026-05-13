using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToastRevival.Api.Services;

public class BillingConfigService : IBillingConfigService
{
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BillingConfigService> _logger;

    public BillingConfigService(
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<BillingConfigService> logger)
    {
        _config = config;
        _env    = env;
        _logger = logger;
    }

    public BillingConfigSnapshot GetSnapshot()
    {
        var priceId     = (_config["Stripe:PerDevicePriceId"] ?? string.Empty).Trim();
        var secretKey   = (_config["Stripe:SecretKey"]        ?? string.Empty).Trim();
        var webhookSec  = (_config["Stripe:WebhookSecret"]    ?? string.Empty).Trim();

        var hasSecretKey    = IsLiveKey(secretKey);
        var hasWebhookSec   = IsWebhookSecret(webhookSec);

        return new BillingConfigSnapshot(
            PerDevicePriceId    : priceId,
            IsConfigured        : IsConfiguredPriceId(priceId),
            PricePerDevice      : BillingPlanRules.PricePerDevice,
            FreeTierDeviceLimit : BillingPlanRules.FreeTierDeviceLimit,
            HasSecretKey        : hasSecretKey,
            HasWebhookSecret    : hasWebhookSec,
            MaskedSecretKey     : hasSecretKey  ? Mask(secretKey)  : null,
            MaskedWebhookSecret : hasWebhookSec ? Mask(webhookSec) : null);
    }

    public Task<BillingConfigSnapshot> UpdateStripeConfigAsync(
        string? secretKey,
        string? webhookSecret,
        string? perDevicePriceId,
        CancellationToken cancellationToken = default)
    {
        if (perDevicePriceId is not null)
            ValidatePriceId(perDevicePriceId.Trim());

        if (secretKey is not null && !secretKey.Trim().StartsWith("sk_", StringComparison.Ordinal))
            throw new ArgumentException("Stripe secret keys start with sk_live_ or sk_test_.");

        if (webhookSecret is not null && !webhookSecret.Trim().StartsWith("whsec_", StringComparison.Ordinal))
            throw new ArgumentException("Stripe webhook secrets start with whsec_.");

        lock (FileLock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = LocalSettingsPath();
            var root = ReadOrCreateRoot(path);

            if (root["Stripe"] is not JsonObject stripe)
            {
                stripe = new JsonObject();
                root["Stripe"] = stripe;
            }

            if (secretKey       is not null) stripe["SecretKey"]       = secretKey.Trim();
            if (webhookSecret   is not null) stripe["WebhookSecret"]   = webhookSecret.Trim();
            if (perDevicePriceId is not null) stripe["PerDevicePriceId"] = perDevicePriceId.Trim();

            File.WriteAllText(path, root.ToJsonString(JsonOptions) + Environment.NewLine);

            if (_config is IConfigurationRoot configRoot)
                configRoot.Reload();

            _logger.LogInformation("Stripe configuration updated via platform admin panel.");
        }

        return Task.FromResult(GetSnapshot());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string LocalSettingsPath() =>
        Path.Combine(_env.ContentRootPath, "appsettings.Local.json");

    private static JsonObject ReadOrCreateRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("appsettings.Local.json must be a JSON object.");
    }

    private static void ValidatePriceId(string priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
            throw new ArgumentException("Enter a Stripe per-device price ID.");
        if (priceId.Length > 128)
            throw new ArgumentException("Stripe price ID is too long.");
        if (!priceId.StartsWith("price_", StringComparison.Ordinal))
            throw new ArgumentException("Stripe price IDs start with price_.");
        if (priceId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Stripe price ID cannot contain spaces.");
    }

    private static bool IsConfiguredPriceId(string? priceId) =>
        !string.IsNullOrWhiteSpace(priceId)
        && priceId.StartsWith("price_", StringComparison.Ordinal)
        && !priceId.StartsWith("price_REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.StartsWith("sk_", StringComparison.Ordinal)
        && !key.StartsWith("sk_test_REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsWebhookSecret(string? secret) =>
        !string.IsNullOrWhiteSpace(secret)
        && secret.StartsWith("whsec_", StringComparison.Ordinal)
        && !secret.StartsWith("whsec_REPLACE", StringComparison.OrdinalIgnoreCase);

    // Show prefix + first 6 chars + **** + last 4
    private static string Mask(string value)
    {
        if (value.Length <= 12) return "****";
        var prefix  = value.StartsWith("sk_live_")  ? "sk_live_"
                    : value.StartsWith("sk_test_")  ? "sk_test_"
                    : value.StartsWith("whsec_")    ? "whsec_"
                    : string.Empty;
        var rest = value[prefix.Length..];
        if (rest.Length <= 8) return prefix + "****";
        return prefix + rest[..4] + "****" + rest[^4..];
    }
}
