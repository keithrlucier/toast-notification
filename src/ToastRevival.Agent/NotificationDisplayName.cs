using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ToastRevival.Agent;

/// <summary>
/// Sets the notification attribution (the small app name shown above every toast)
/// to the tenant's display name.
///
/// Two-phase mechanism because the Windows App SDK manages its own AUMID for
/// unpackaged apps and does NOT honor SetCurrentProcessExplicitAppUserModelID:
///
///   Phase 1 — <see cref="Apply"/>: called BEFORE AppNotificationManager.Default
///   touches anything. Sets our explicit AUMID via SetCurrentProcessExplicitAppUserModelID
///   and pre-writes DisplayName + IconUri under that AUMID. This wins for any code
///   path that respects the legacy Shell32 AUMID (e.g. taskbar pinning, jump lists).
///
///   Phase 2 — <see cref="ApplyToActivatorAumids"/>: called AFTER
///   AppNotificationManager.Default.Register() returns. The SDK creates a
///   private AUMID for unpackaged apps and writes our COM activator CLSID
///   into HKCU\SOFTWARE\Classes\AppUserModelId\{sdk-aumid}\CustomActivator.
///   We scan that hive, find every AUMID whose CustomActivator matches our
///   toast activator CLSID, and overwrite DisplayName + IconUri on each.
///   This is what Windows reads when it renders the toast.
///
/// The two-phase approach is the most robust shape: we write to BOTH our
/// declared AUMID and the SDK's hash-derived AUMID, so the tenant name shows
/// up whichever code path Windows actually queries.
/// </summary>
internal static class NotificationDisplayName
{
    private const string AumidBase = "Toast2IT.ToastNotification";

    /// <summary>
    /// COM activator CLSID from <c>Package.appxmanifest</c> / the runtime registration
    /// AppNotificationManager.Default.Register() writes for unpackaged toast delivery.
    /// Used in Phase 2 to find the SDK's hash-derived AUMID under HKCU.
    /// </summary>
    private const string ActivatorClsid = "7FA7762F-41EC-4D72-9F06-58964AB36FEA";

    private const string AumidHiveRoot = @"SOFTWARE\Classes\AppUserModelId";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    /// <summary>
    /// Phase 1 — sets the process explicit AUMID and writes our declared AUMID's
    /// DisplayName + IconUri. Must be called BEFORE any AppNotificationManager.Default
    /// access. Does not affect what AppNotificationManager.Default.Register() ends
    /// up writing for the toast activator — Phase 2 handles that.
    /// </summary>
    public static void Apply(string? tenantName)
    {
        var displayName = ResolveDisplayName(tenantName);
        var logoPath = TryFindLogoPath();

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AumidBase);

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{AumidHiveRoot}\{AumidBase}", writable: true);
            key.SetValue("DisplayName", displayName);
            if (logoPath is not null)
                key.SetValue("IconUri", logoPath);

            DiagLog.Write($"NotificationDisplayName.Apply: explicit AUMID '{AumidBase}', DisplayName='{displayName}', icon={(logoPath ?? "(missing)")}");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.Apply: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Phase 2 — scan HKCU\SOFTWARE\Classes\AppUserModelId for entries whose
    /// <c>CustomActivator</c> value matches our toast activator CLSID, and
    /// write DisplayName + IconUri on each. Must be called AFTER
    /// AppNotificationManager.Default.Register() returns, because Register()
    /// is what creates those entries for unpackaged apps.
    /// </summary>
    public static void ApplyToActivatorAumids(string? tenantName)
    {
        var displayName = ResolveDisplayName(tenantName);
        var logoPath = TryFindLogoPath();

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(AumidHiveRoot, writable: false);
            if (root is null)
            {
                DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: HKCU\\{AumidHiveRoot} not found");
                return;
            }

            var matches = 0;
            foreach (var aumid in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(aumid, writable: true);
                if (sub is null) continue;

                var customActivator = sub.GetValue("CustomActivator")?.ToString();
                if (string.IsNullOrEmpty(customActivator)) continue;

                if (!ActivatorClsidsMatch(customActivator, ActivatorClsid)) continue;

                sub.SetValue("DisplayName", displayName);
                if (logoPath is not null)
                    sub.SetValue("IconUri", logoPath);

                DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: matched AUMID '{aumid}' (CustomActivator={customActivator}), DisplayName='{displayName}'");
                matches++;
            }

            DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: {matches} AUMID(s) matched activator CLSID '{ActivatorClsid}'");

            if (matches == 0)
            {
                // No match means AppNotificationManager.Default.Register() didn't
                // write our activator under any AUMID — diagnostic dump so we can
                // see what SDK actually wrote and adjust the matcher.
                DumpAumidHive(root);
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ResolveDisplayName(string? tenantName) =>
        !string.IsNullOrWhiteSpace(tenantName) ? tenantName.Trim() : "Toast Notification";

    private static string? TryFindLogoPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "toast-logo.png");
        return File.Exists(path) ? path : null;
    }

    private static bool ActivatorClsidsMatch(string registryValue, string expected)
    {
        var a = registryValue.Trim().Trim('{', '}');
        var b = expected.Trim().Trim('{', '}');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks the AUMID hive and logs every subkey + its CustomActivator + DisplayName.
    /// Only invoked when Phase 2 finds zero matches, so the cost is paid once per
    /// (presumably broken) agent start and the operator can read the log to see
    /// what AppNotificationManager actually wrote.
    /// </summary>
    private static void DumpAumidHive(RegistryKey root)
    {
        try
        {
            DiagLog.Write("NotificationDisplayName: AUMID hive dump start");
            foreach (var aumid in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(aumid, writable: false);
                if (sub is null) continue;
                var act = sub.GetValue("CustomActivator")?.ToString() ?? "(none)";
                var disp = sub.GetValue("DisplayName")?.ToString() ?? "(none)";
                DiagLog.Write($"  {aumid} | CustomActivator={act} | DisplayName={disp}");
            }
            DiagLog.Write("NotificationDisplayName: AUMID hive dump end");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.DumpAumidHive: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }
}
