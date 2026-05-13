namespace ToastRevival.Api.Services;

public interface IBillingConfigService
{
    BillingConfigSnapshot GetSnapshot();
    Task<BillingConfigSnapshot> UpdateStripeConfigAsync(
        string? secretKey,
        string? webhookSecret,
        string? perDevicePriceId,
        CancellationToken cancellationToken = default);
}

public sealed record BillingConfigSnapshot(
    string  PerDevicePriceId,
    bool    IsConfigured,
    decimal PricePerDevice,
    int     FreeTierDeviceLimit,
    bool    HasSecretKey,
    bool    HasWebhookSecret,
    string? MaskedSecretKey,
    string? MaskedWebhookSecret);
