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
// which Server 2025 does enumerate.
//
// ACTIVATION (button/body clicks): because the toast is dispatched through the
// legacy WinRT API, AppNotificationManager.Default.NotificationInvoked does NOT
// fire for it — that event is only raised for notifications shown via
// AppNotificationManager.Show(). The legacy ToastNotification instead raises its
// OWN Activated event, delivered IN-PROCESS to the showing process while it is
// alive. The tray agent is always alive when a toast is on screen (it just
// showed it), so subscribing to ToastNotification.Activated here is the live
// click path. Cold activation from Action Center when the agent is NOT running
// still needs a proper INotificationActivationCallback COM activator +
// CLSID\LocalServer32 registration (the appxmanifest declares it, but that file
// is only consumed by MSIX builds — the unpackaged MSI install has no such
// registration). That cold path is tracked as a separate follow-up.
internal static class LegacyToastShim
{
    private const string Aumid = "Toast2IT.ToastNotification";

    // Roots live ToastNotification instances so the managed wrapper — and the
    // Activated/Dismissed/Failed delegates hanging off it — survive until the
    // user acts on or dismisses the banner. Without this root, GC can collect
    // the wrapper (and its event subscriptions) while the banner is still on
    // screen, so the click would arrive with nothing listening.
    private static readonly HashSet<ToastNotification> _live = new();
    private static readonly object _liveLock = new();

    /// <param name="onActivated">
    /// Invoked with the clicked action's argument string (same key=value;key=value
    /// shape AppNotificationBuilder.AddArgument encoded) when the user clicks the
    /// toast or one of its buttons. Null for fire-and-forget toasts (e.g. the tray
    /// connectivity test) that carry no actionable arguments.
    /// </param>
    public static void Show(AppNotification notification, Action<string>? onActivated = null)
    {
        var doc = new XmlDocument();
        doc.LoadXml(notification.Payload);

        var legacy = new ToastNotification(doc);

        if (onActivated is not null)
        {
            legacy.Activated += (sender, args) =>
            {
                var arguments = (args as ToastActivatedEventArgs)?.Arguments ?? string.Empty;
                Untrack(sender);
                try { onActivated(arguments); }
                catch (Exception ex) { DiagLog.Write($"LegacyToastShim: Activated handler threw: {ex.GetType().Name}: {ex.Message}"); }
            };
        }

        legacy.Dismissed += (sender, _) => Untrack(sender);
        legacy.Failed    += (sender, _) => { DiagLog.Write("LegacyToastShim: toast Failed."); Untrack(sender); };

        Track(legacy);

        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
            notifier.Show(legacy);
        }
        catch
        {
            // Show threw — the banner never went up, so nothing will ever
            // Dismiss/Activate it. Drop the root immediately so it can't leak.
            Untrack(legacy);
            throw;
        }
    }

    private static void Track(ToastNotification n)
    {
        lock (_liveLock) { _live.Add(n); }
    }

    private static void Untrack(ToastNotification n)
    {
        lock (_liveLock) { _live.Remove(n); }
    }
}
