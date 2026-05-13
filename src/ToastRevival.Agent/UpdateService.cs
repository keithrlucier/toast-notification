using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using Velopack;

namespace ToastRevival.Agent;

/// <summary>
/// Velopack auto-update service for the MSI/RMM deployment channel.
///
/// Architecture notes:
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
/// Known limitations:
///   - LocalAppData cleanup on MSI uninstall: when MSI uninstalls the
///     ProgramFiles copy, any Velopack-managed files at
///     %LocalAppData%\ToastNotification.Agent\ remain as orphans. The MSI CA
///     runs as SYSTEM and cannot access user-scoped LocalAppData. Cleanup is
///     left to the user-context VelopackApp WithBeforeUninstall hook when the
///     Velopack channel itself uninstalls.
///   - First logon after initial Velopack update still starts from
///     %ProgramFiles%. The scheduled task always launches the ProgramFiles
///     bootstrap binary. On first logon after a Velopack update,
///     TrySelfRedirect fires and relaunches from %LocalAppData% (two process
///     starts that logon, ~50ms cost). From the second logon onward the user
///     runs purely from the Velopack-managed path.
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

        // Verify the managed binary is signed by Toast2IT, LLC before
        // launching. Prevents a local-user attacker from planting a
        // higher-versioned malicious binary at the Velopack managed path.
        if (!IsSignedByToast2IT(managedExe))
        {
            DiagLog.Write($"UpdateService: managed copy at '{managedExe}' failed Authenticode verification — redirect aborted.");
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

    /// <summary>
    /// Verifies that <paramref name="filePath"/> carries a valid Authenticode
    /// signature issued by Toast2IT, LLC using WinVerifyTrust + cert subject check.
    /// Returns false on any failure so callers default to safe/no-redirect.
    /// </summary>
    private static bool IsSignedByToast2IT(string filePath)
    {
        const string expectedSubject = "Toast2IT, LLC";
        try
        {
            // Step 1: cert subject check (fast, no network) — rejects unsigned binaries
            using var cert = X509Certificate2.CreateFromSignedFile(filePath);
            if (!cert.Subject.Contains(expectedSubject, StringComparison.OrdinalIgnoreCase))
            {
                DiagLog.Write($"UpdateService: cert subject mismatch — expected '{expectedSubject}', got '{cert.Subject}'.");
                return false;
            }

            // Step 2: full chain + signature validation via WinVerifyTrust
            return WinVerifyTrustResult(filePath) == 0; // 0 = valid
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdateService: Authenticode check failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static uint WinVerifyTrustResult(string filePath)
    {
        var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2

        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

        var trustData = new WINTRUST_DATA
        {
            cbStruct            = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData      = IntPtr.Zero,
            dwUIChoice          = 2,  // WTD_UI_NONE
            fdwRevocationChecks = 0,  // WTD_REVOKE_NONE
            dwUnionChoice       = 1,  // WTD_CHOICE_FILE
            pFile               = fileInfoPtr,
            dwStateAction       = 1,  // WTD_STATEACTION_VERIFY
            hWVTStateData       = IntPtr.Zero,
            pwszURLReference    = null,
            dwUIContext         = 0,
        };

        var trustDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        Marshal.StructureToPtr(trustData, trustDataPtr, false);

        uint result;
        try
        {
            result = WinVerifyTrust(IntPtr.Zero, ref actionId, trustDataPtr);

            // Close the state action to release resources
            trustData.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            Marshal.StructureToPtr(trustData, trustDataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref actionId, trustDataPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(trustDataPtr);
        }

        return result;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U4)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint   cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint   cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint   dwUIChoice;
        public uint   fdwRevocationChecks;
        public uint   dwUnionChoice;
        public IntPtr pFile;
        public uint   dwStateAction;
        public IntPtr hWVTStateData;
        public string? pwszURLReference;
        public uint   dwUIContext;
    }
}
