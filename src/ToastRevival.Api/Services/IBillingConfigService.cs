namespace ToastRevival.Api.Services;

public interface IBillingConfigService
{
    BillingConfigSnapshot GetSnapshot();
    Task<BillingConfigSnapshot> UpdatePerDevicePriceIdAsync(string? perDevicePriceId, CancellationToken cancellationToken = default);
}

public sealed record BillingConfigSnapshot(
    string PerDevicePriceId,
    bool IsConfigured,
    decimal PricePerDevice,
    int MinimumDevices,
    decimal MonthlyFloor);
