using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ToastRevival.Agent;

/// <summary>
/// Overrides the notification attribution (the small app name shown above
/// every toast) with the tenant's display name.
///
/// Evidence-based design (2026-05-12, agent.log + HKCU hive dump from the
/// Colo Solutions test machine). WinAppSDK AppNotificationManager for
/// unpackaged apps does the following at <c>Register()</c> time:
///
///   1. Generates its OWN COM activator CLSID (NOT the one in our
///      Package.appxmanifest — that file is only consumed by MSIX builds).
///   2. Creates a GUID-keyed AUMID — <c>HKCU\Software\Classes\AppUserModelId\{guid}</c>
///      — and writes DisplayName + IconUri + CustomActivator under it.
///      DisplayName is auto-derived from AssemblyName (stripped of trailing
///      <c>.Agent</c>); IconUri points at a PNG in
///      <c>%LOCALAPPDATA%\Microsoft\WindowsAppSDK</c>.
///   3. Creates a path-alias key —
///      <c>HKCU\Software\Classes\AppUserModelId\&lt;normalized-exe-path&gt;</c>
///      — whose <c>NotificationGUID</c> value points at the GUID-keyed AUMID
///      from step 2. Path normalization is lowercase + replace
///      backslashes/forward-slashes with dots.
///
/// When Windows renders a toast, it looks up the attribution by normalizing
/// the running exe's path, reading <c>NotificationGUID</c> from the
/// path-alias key, then reading <c>DisplayName</c> from the GUID-keyed AUMID.
/// <see cref="SetCurrentProcessExplicitAppUserModelID"/> only affects the
/// legacy Shell32 AUMID (jump lists / taskbar pinning) — NOT what the
/// WinAppSDK toast subsystem reads.
///
/// To override the attribution we therefore must, AFTER
/// <c>AppNotificationManager.Default.Register()</c> returns:
///   a. Compute the path-alias the same way WinAppSDK does.
///   b. Read <c>NotificationGUID</c> from the path-alias key.
///   c. Overwrite <c>DisplayName</c> + <c>IconUri</c> on the GUID-keyed AUMID.
///
/// We continue to set the explicit AUMID in Phase 1 (legacy Shell32 paths)
/// and we dump the AUMID hive to DiagLog on every startup so any future
/// WinAppSDK schema drift is visible without another guess-and-ship cycle.
/// </summary>
internal static class NotificationDisplayName
{
    private const string AumidBase = "Toast2IT.ToastNotification";
    private const string AumidHiveRoot = @"SOFTWARE\Classes\AppUserModelId";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    /// <summary>
    /// Phase 1 — sets the explicit process AUMID and writes DisplayName + IconUri
    /// under our declared AUMID. Must run BEFORE any AppNotificationManager.Default
    /// touch. This wins for legacy Shell32 AUMID consumers (jump lists, taskbar pin)
    /// but does NOT control the WinAppSDK toast subsystem — Phase 2 handles that.
    ///
    /// <paramref name="customLogoPath"/> overrides the bundled fallback when the
    /// tenant has uploaded their own logo via Tenant Settings — the agent
    /// downloads that logo to local disk via <see cref="TenantLogoStore"/>
    /// before this call. Falls back to Assets\toast-logo.png when null.
    /// </summary>
    public static void Apply(string? tenantName, string? customLogoPath = null)
    {
        var displayName = ResolveDisplayName(tenantName);
        var logoPath = ResolveLogoPath(customLogoPath);

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AumidBase);

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{AumidHiveRoot}\{AumidBase}", writable: true);
            // Match WinAppSDK's value kind so any consumer that distinguishes
            // REG_SZ from REG_EXPAND_SZ sees a consistent shape.
            key.SetValue("DisplayName", displayName, RegistryValueKind.ExpandString);
            if (logoPath is not null)
                key.SetValue("IconUri", logoPath, RegistryValueKind.ExpandString);

            DiagLog.Write($"NotificationDisplayName.Apply: declared AUMID '{AumidBase}', DisplayName='{displayName}', icon={(logoPath ?? "(missing)")}");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.Apply: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Phase 2 — finds the AUMID Windows actually reads when rendering toasts
    /// and overwrites its DisplayName + IconUri. Must run AFTER
    /// <c>AppNotificationManager.Default.Register()</c> returns, because Register()
    /// is what creates the path-alias and GUID-keyed AUMID entries (and would
    /// otherwise re-overwrite any DisplayName we set earlier).
    /// </summary>
    public static void ApplyToActivatorAumids(string? tenantName, string? customLogoPath = null)
    {
        var displayName = ResolveDisplayName(tenantName);
        var logoPath = ResolveLogoPath(customLogoPath);

        try
        {
            // Always dump the hive while we're stabilizing this behavior across
            // WinAppSDK versions — gives operators a single artifact to grep when
            // something doesn't show up the way it should.
            DumpAumidHive();

            var exePath = ResolveExePath();
            if (exePath is null)
            {
                DiagLog.Write("NotificationDisplayName.ApplyToActivatorAumids: could not resolve current process exe path; aborting Phase 2.");
                return;
            }

            var pathAlias = NormalizeExePathForAumidAlias(exePath);
            DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: exe='{exePath}', path-alias='{pathAlias}'");

            string? targetGuidAumid = null;
            using (var aliasKey = Registry.CurrentUser.OpenSubKey($@"{AumidHiveRoot}\{pathAlias}", writable: false))
            {
                if (aliasKey is null)
                {
                    DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: path-alias key not found under HKCU\\{AumidHiveRoot}\\{pathAlias}. Falling back to declared-AUMID write only.");
                }
                else
                {
                    targetGuidAumid = aliasKey.GetValue("NotificationGUID")?.ToString();
                    if (string.IsNullOrEmpty(targetGuidAumid))
                    {
                        DiagLog.Write("NotificationDisplayName.ApplyToActivatorAumids: path-alias key has no NotificationGUID value.");
                    }
                    else
                    {
                        DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: resolved NotificationGUID='{targetGuidAumid}' for this exe.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(targetGuidAumid))
            {
                WriteDisplayNameAt(targetGuidAumid!, displayName, logoPath);
            }

            // Belt-and-suspenders: write under our declared AUMID too, in case any
            // code path (legacy compat, future SDK refactor, third-party consumer)
            // reads from there instead of the path-alias resolution.
            WriteDisplayNameAt(AumidBase, displayName, logoPath);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.ApplyToActivatorAumids: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ResolveDisplayName(string? tenantName) =>
        !string.IsNullOrWhiteSpace(tenantName) ? tenantName.Trim() : "Toast Notification";

    /// <summary>
    /// Tenant-uploaded logo (downloaded to local disk by <see cref="TenantLogoStore"/>)
    /// wins over the bundled fallback. The caller passes null when the tenant
    /// hasn't configured a logo or the download failed — we then degrade to the
    /// agent's shipped Assets\toast-logo.png so the tiny attribution icon never
    /// renders as a blank slot.
    /// </summary>
    private static string? ResolveLogoPath(string? customLogoPath)
    {
        if (!string.IsNullOrWhiteSpace(customLogoPath) && File.Exists(customLogoPath))
            return customLogoPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "toast-logo.png");
        return File.Exists(bundled) ? bundled : null;
    }

    private static string? ResolveExePath()
    {
        // Environment.ProcessPath returns the absolute path of the host on .NET 6+.
        var p = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(p)) return p;

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors WinAppSDK's path-alias normalization (verified against
    /// HKCU dump 2026-05-12): lowercase, replace <c>\</c> and <c>/</c>
    /// with <c>.</c>, leave spaces and other characters intact.
    /// </summary>
    private static string NormalizeExePathForAumidAlias(string exePath) =>
        exePath.ToLowerInvariant().Replace('\\', '.').Replace('/', '.');

    private static void WriteDisplayNameAt(string aumidKeyName, string displayName, string? logoPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"{AumidHiveRoot}\{aumidKeyName}", writable: true);
            key.SetValue("DisplayName", displayName, RegistryValueKind.ExpandString);
            if (logoPath is not null)
                key.SetValue("IconUri", logoPath, RegistryValueKind.ExpandString);
            DiagLog.Write($"NotificationDisplayName.WriteDisplayNameAt: '{aumidKeyName}' DisplayName='{displayName}' icon={(logoPath ?? "(missing)")}");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.WriteDisplayNameAt: '{aumidKeyName}' failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Walks HKCU\Software\Classes\AppUserModelId and logs every subkey's
    /// CustomActivator / NotificationGUID / DisplayName. Cheap (a few dozen
    /// values total in a typical hive) and runs once per agent startup;
    /// keeps an artifact in DiagLog the operator can paste back when an
    /// attribution issue recurs so we never debug this blind again.
    /// </summary>
    private static void DumpAumidHive()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(AumidHiveRoot, writable: false);
            if (root is null)
            {
                DiagLog.Write($"NotificationDisplayName.DumpAumidHive: HKCU\\{AumidHiveRoot} not present.");
                return;
            }

            DiagLog.Write("NotificationDisplayName.DumpAumidHive: BEGIN");
            foreach (var aumid in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(aumid, writable: false);
                if (sub is null) continue;
                var disp = sub.GetValue("DisplayName")?.ToString();
                var act = sub.GetValue("CustomActivator")?.ToString();
                var ng  = sub.GetValue("NotificationGUID")?.ToString();
                DiagLog.Write($"  [{aumid}] DisplayName={disp ?? "(none)"} | CustomActivator={act ?? "(none)"} | NotificationGUID={ng ?? "(none)"}");
            }
            DiagLog.Write("NotificationDisplayName.DumpAumidHive: END");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NotificationDisplayName.DumpAumidHive: failed — {ex.GetType().Name}: {ex.Message}");
        }
    }
}
