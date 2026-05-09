namespace ToastRevival.Api.Services;

public static class BillingPlanRules
{
    public const decimal PricePerDevice = 0.22m;
    public const int MinimumBillableDevices = 100;
    public const decimal MonthlyFloor = PricePerDevice * MinimumBillableDevices;

    public static int BillableDevices(int activeDeviceCount) =>
        Math.Max(MinimumBillableDevices, Math.Max(0, activeDeviceCount));

    public static decimal CurrentBill(int activeDeviceCount) =>
        BillableDevices(activeDeviceCount) * PricePerDevice;
}
