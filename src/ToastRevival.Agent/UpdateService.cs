using Microsoft.Win32;
using Velopack;

namespace ToastRevival.Agent;

/// <summary>
/// Velopack auto-update service for the MSI/RMM deployment channel.
///
/// Architecture note (M2.D):
///   - Only active when running as a Velopack-managed install (IsInstalled == true).
///     MSI-bootstrapped installs at %ProgramFiles% are not Velopack-managed and do
///     not self-update — that is intentional and correct for MSP environments where
///     RMM tools own the update lifecycle.
///   - TrySelfRedirect: if somehow the bootstrap binary at %ProgramFiles% starts
///     and a newer Velopack-managed copy exists in %LocalAppData%, it launches the
///     managed copy and exits. Handles the scheduled-task pointer problem on logons
///     after the first Velopack-channel update.
///   - Enterprise MSPs can set HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate=1
///     to suppress all update activity. IT can also set UpdateFeedUrl to an internal mirror.
///
/// INFO-M2D-001: LocalAppData cleanup on MSI uninstall. When MSI uninstalls the
///   ProgramFiles copy, any Velopack-managed files at %LocalAppData%\ToastNotification.Agent\
///   remain as orphans. The MSI CA runs as SYSTEM and cannot access user-scoped LocalAppData.
///   Fix deferred to M9: add a user-context CA or VelopackApp.Build().WithBeforeUninstall()
///   hook that cleans up the LocalAppData tree when Velopack itself uninstalls.
///
/// INFO-M2D-002: First logon after initial Velopack update still starts from %ProgramFiles%.
///   The scheduled task always launches the ProgramFiles bootstrap binary. On first logon
///   after a Velopack update, TrySelfRedirect fires and relaunches from %LocalAppData%
///   (two process starts that logon, ~50ms cost). From the second logon onward the
///   user runs purely from the Velopack-managed path. Acceptable for MSP context.
/// </summary>
internal static class UpdateService
{
    private const string RegistryKeyPath  = @"SOFTWARE\Toast2IT\Toast Notification";
    private const string DefaultFeedUrl   = "https://releases.toastnotification.com/agent/win-x64";
    private const string VelopackPackId   = "ToastNotification.Agent";

    private static UpdateInfo? _pendingUpdate;

    public static string? PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();

    /// <summary>
    /// Raised on a thread-pool thread when a download completes successfully.
    /// Wire to tray.ShowUpdateAvailable(version).
    /// </summary>
    public static event Action<string>? UpdateReady;

    public static bool IsAutoUpdateEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue("DisableAutoUpdate") is int v && v != 0) return false;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdateService: registry read error: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    private static string GetFeedUrl()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue("UpdateFeedUrl") is string url && !string.IsNullOrWhiteSpace(url))
                return url;
        }
        catch { /* fall through to default */ }
        return DefaultFeedUrl;
    }

    /// <summary>
    /// Called at startup (before mode dispatch). If running from %ProgramFiles% and a
    /// newer Velopack-managed binary exists in %LocalAppData%\ToastNotification.Agent\current\,
    /// launches the managed copy with the same args and returns true — caller must exit 0.
    ///
    /// Guards:
    ///  - Only redirects from a system-install path (ProgramFiles). Already running from
    ///    the managed path is the normal steady-state after first update.
    ///  - Version check prevents an infinite redirect loop if versions are equal.
    /// </summary>
    public static bool TrySelfRedirect(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var pf64    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        bool isSystemInstall =
            baseDir.StartsWith(pf64, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(pf86) && baseDir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase));

        if (!isSystemInstall) return false;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var managedExe   = Path.Combine(localAppData, VelopackPackId, "current", "ToastNotification.Agent.exe");

        if (!File.Exists(managedExe)) return false;

        var ourVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        var fvi        = System.Diagnostics.FileVersionInfo.GetVersionInfo(managedExe);
        if (!Version.TryParse(fvi.FileVersion, out var managedVer) || managedVer <= ourVersion)
        {
            DiagLog.Write($"UpdateService: managed copy v{fvi.FileVersion} not newer than {ourVersion} — no redirect.");
            return false;
        }

        DiagLog.Write($"UpdateService: redirecting to managed v{managedVer} at '{managedExe}'");
        try
        {
            var safeArgs = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName         = managedExe,
                Arguments        = safeArgs,
                UseShellExecute  = false,
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdateService: redirect launch failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Background update loop. Checks once at startup then every 24 hours.
    /// No-op if auto-update is disabled or if not running as a Velopack-managed install.
    /// </summary>
    public static async Task RunUpdateLoopAsync(CancellationToken ct)
    {
        if (!IsAutoUpdateEnabled())
        {
            DiagLog.Write("UpdateService: disabled via registry — no update checks.");
            return;
        }

        try { await CheckAndDownloadAsync(ct); }
        catch (Exception ex) { DiagLog.Write($"UpdateService: startup check failed: {ex.GetType().Name}: {ex.Message}"); }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try { await CheckAndDownloadAsync(ct); }
            catch (Exception ex) { DiagLog.Write($"UpdateService: periodic check failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static async Task CheckAndDownloadAsync(CancellationToken ct)
    {
        var mgr = new UpdateManager(GetFeedUrl());

        if (!mgr.IsInstalled)
        {
            // Running from %ProgramFiles% (MSI bootstrap path). Velopack is not managing
            // this install, so we cannot safely apply updates here. MSPs manage the update
            // lifecycle via RMM for MSI-deployed agents. The TrySelfRedirect path handles
            // the case where a Velopack-managed version was previously installed separately.
            DiagLog.Write("UpdateService: not a Velopack-managed install — update check skipped.");
            return;
        }

        var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
        {
            DiagLog.Write($"UpdateService: up to date at v{mgr.CurrentVersion}.");
            return;
        }

        DiagLog.Write($"UpdateService: v{info.TargetFullRelease.Version} available — downloading.");
        await mgr.DownloadUpdatesAsync(info, p => DiagLog.Write($"UpdateService: download {p}%"), ct).ConfigureAwait(false);
        DiagLog.Write($"UpdateService: v{info.TargetFullRelease.Version} ready to apply.");

        _pendingUpdate = info;
        UpdateReady?.Invoke(info.TargetFullRelease.Version.ToString());
    }

    /// <summary>
    /// Apply the downloaded update and restart the agent. Called from tray "Apply Update" click.
    /// Calls Environment.Exit internally via Velopack — nothing after this call runs.
    /// </summary>
    public static void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate is null)
        {
            DiagLog.Write("UpdateService: ApplyUpdateAndRestart called with no pending update — ignored.");
            return;
        }

        DiagLog.Write($"UpdateService: applying v{_pendingUpdate.TargetFullRelease.Version} and restarting.");
        var mgr = new UpdateManager(GetFeedUrl());
        mgr.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
