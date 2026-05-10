namespace ToastRevival.Api.Services;

public static class BillingPlanRules
{
    public const decimal PricePerDevice      = 0.22m;
    public const int     FreeTierDeviceLimit = 25;   // devices 1-25 are always free

    // Billable count is everything above the free tier, floor zero.
    // No monthly minimum — first paid device is device 26.
    public static int BillableDevices(int activeDeviceCount) =>
        Math.Max(0, activeDeviceCount - FreeTierDeviceLimit);

    public static decimal CurrentBill(int activeDeviceCount) =>
        BillableDevices(activeDeviceCount) * PricePerDevice;
}
