using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ToastRevival.Agent;

/// <summary>
/// Sets the notification attribution (the small app name shown above every toast)
/// to the tenant's display name. Must be called before AppNotificationManager.Register().
///
/// Mechanism: Windows derives the display name from the AUMID registered in
/// HKCU\SOFTWARE\Classes\AppUserModelId\{aumid}\DisplayName. We set the AUMID
/// explicitly via SetCurrentProcessExplicitAppUserModelID, then write the tenant
/// name as the display name so all toasts show "Colo Solutions" (or whatever
/// the tenant is named) instead of the generic exe name.
/// </summary>
internal static class NotificationDisplayName
{
    private const string AumidBase = "Toast2IT.ToastNotification";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    public static void Apply(string? tenantName)
    {
        var displayName = !string.IsNullOrWhiteSpace(tenantName)
            ? tenantName.Trim()
            : "Toast Notification";

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AumidBase);

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Classes\AppUserModelId\{AumidBase}", writable: true);
            key.SetValue("DisplayName", displayName);

            // Point the icon at the app logo so the notification header shows
            // our icon alongside the tenant name.
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "toast-logo.png");
            if (File.Exists(logoPath))
                key.SetValue("IconUri", logoPath);

            DiagLog.Write($"NotificationDisplayName: set to '{displayName}'");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }
}
