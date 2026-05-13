using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Microsoft.Windows.AppNotifications;

namespace ToastRevival.Agent;

// Server 2025 registers notification senders via the legacy WinRT API only.
// WinAppSDK's AppNotificationManager.Show() succeeds from the SDK's perspective
// but is silently dropped before reaching the banner or Action Center because
// Server 2025 doesn't enumerate apps registered through the newer API path.
// This shim extracts the XML payload built by AppNotificationBuilder and
// re-dispatches it through Windows.UI.Notifications.ToastNotificationManager,
// which Server 2025 does enumerate.  AppNotificationManager.Default.Register()
// is still called by the caller for COM activation (button-click round-trips).
internal static class LegacyToastShim
{
    private const string Aumid = "Toast2IT.ToastNotification";

    public static void Show(AppNotification notification)
    {
        var doc = new XmlDocument();
        doc.LoadXml(notification.Payload);

        var legacy = new ToastNotification(doc);
        var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
        notifier.Show(legacy);
    }
}
