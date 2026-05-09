using Stripe;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class StripeBillingSyncService : IStripeBillingSyncService
{
    private readonly IConfiguration _config;
    private readonly ILogger<StripeBillingSyncService> _logger;

    public StripeBillingSyncService(IConfiguration config, ILogger<StripeBillingSyncService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SyncSubscriptionQuantityAsync(Tenant tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId)) return;

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            return;

        try
        {
            StripeConfiguration.ApiKey = secretKey;

            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(tenant.StripeSubscriptionId, cancellationToken: ct);
            var subscriptionItem = subscription.Items?.Data?.FirstOrDefault();
            if (subscriptionItem is null)
            {
                _logger.LogWarning("Stripe subscription {SubscriptionId} has no subscription item.", tenant.StripeSubscriptionId);
                return;
            }

            var billableDevices = BillingPlanRules.BillableDevices(tenant.ConsumedCount);
            var itemService = new SubscriptionItemService();
            await itemService.UpdateAsync(
                subscriptionItem.Id,
                new SubscriptionItemUpdateOptions { Quantity = billableDevices },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sync Stripe subscription quantity for tenant {TenantId}.", tenant.Id);
        }
    }
}
