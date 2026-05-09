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
        _env = env;
        _logger = logger;
    }

    public BillingConfigSnapshot GetSnapshot()
    {
        var priceId = (_config["Stripe:PerDevicePriceId"] ?? string.Empty).Trim();
        return new BillingConfigSnapshot(
            priceId,
            IsConfiguredPriceId(priceId),
            BillingPlanRules.PricePerDevice,
            BillingPlanRules.MinimumBillableDevices,
            BillingPlanRules.MonthlyFloor);
    }

    public Task<BillingConfigSnapshot> UpdatePerDevicePriceIdAsync(
        string? perDevicePriceId,
        CancellationToken cancellationToken = default)
    {
        var priceId = perDevicePriceId?.Trim() ?? string.Empty;
        ValidatePriceId(priceId);

        lock (FileLock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = LocalSettingsPath();
            var root = ReadOrCreateRoot(path);
            var stripe = root["Stripe"] as JsonObject;
            if (stripe is null)
            {
                stripe = new JsonObject();
                root["Stripe"] = stripe;
            }

            stripe["PerDevicePriceId"] = priceId;

            var json = root.ToJsonString(JsonOptions) + Environment.NewLine;
            File.WriteAllText(path, json);

            if (_config is IConfigurationRoot configurationRoot)
                configurationRoot.Reload();

            _logger.LogInformation("Stripe per-device price ID updated through platform admin billing config.");
        }

        return Task.FromResult(GetSnapshot());
    }

    private string LocalSettingsPath() =>
        Path.Combine(_env.ContentRootPath, "appsettings.Local.json");

    private static JsonObject ReadOrCreateRoot(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        var node = JsonNode.Parse(text);
        return node as JsonObject
            ?? throw new InvalidOperationException("appsettings.Local.json must contain a JSON object.");
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
}
