using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ToastRevival.Agent;

/// <summary>
/// MSI self-update and remote uninstall for the MSI/RMM deployment channel.
///
/// MSI-installed agents live at %ProgramFiles% and are not Velopack-managed
/// (UpdateManager.IsInstalled == false). This service fills the update gap for
/// that channel by periodically polling the server for a newer version, downloading
/// the signed MSI to %ProgramData%, then delegating the actual msiexec call to
/// the SYSTEM-running ToastNotificationUpdater scheduled task (writing a trigger
/// file and calling schtasks /Run).
///
/// The same SYSTEM task handles remote uninstalls requested from the admin panel
/// via the "UninstallAgent" hub command.
///
/// WiX MajorUpgrade AllowSameVersionUpgrades="yes" makes over-the-top MSI install
/// a clean in-place upgrade — one ARP entry, existing ProductCode replaced.
///
/// Architecture note:
///   Agent (user context, LeastPrivilege) ──writes──► trigger file
///   schtasks /Run ─────────────────────────────────► ToastNotificationUpdater task
///   Task (SYSTEM) ──launches──► agent.exe --run-updater ──reads trigger──► msiexec
///
/// Separation is required because per-machine msiexec needs SYSTEM/Admin rights
/// that the agent deliberately does not hold.
/// </summary>
internal static class SelfUpdateService
{
    private const string RegistryKeyPath = @"SOFTWARE\Toast2IT\Toast Notification";
    private const string UpdaterTaskName = @"\Toast2IT\ToastNotificationUpdater";
    private const string TriggerFileName = "pending-action.txt";
    private const string UpdateSubDir    = "update";
    private const string UpdatedMsiName  = "ToastNotification.Agent.msi";
    private const string ExpectedSubject = "Toast2IT, LLC";
    private const long   MaxMsiBytes     = 200 * 1024 * 1024; // 200 MB ceiling

    // ─── MSI update loop ────────────────────────────────────────────────────

    /// <summary>
    /// Polls /api/agent/version once at startup then every 24h. No-op for
    /// Velopack-managed installs (they use UpdateService). Respects
    /// DisableAutoUpdate registry toggle.
    /// </summary>
    public static async Task RunMsiUpdateLoopAsync(DeviceConfig config, CancellationToken ct)
    {
        // Velopack owns this install — don't double-update.
        var mgr = new Velopack.UpdateManager(string.Empty);
        if (mgr.IsInstalled)
        {
            DiagLog.Write("SelfUpdateService: Velopack-managed install — MSI update loop skipped.");
            return;
        }

        if (!IsAutoUpdateEnabled())
        {
            DiagLog.Write("SelfUpdateService: DisableAutoUpdate set — MSI update loop skipped.");
            return;
        }

        try { await CheckAndTriggerAsync(config, ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: startup check failed: {ex.GetType().Name}: {ex.Message}");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try { await CheckAndTriggerAsync(config, ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                DiagLog.Write($"SelfUpdateService: periodic check failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static async Task CheckAndTriggerAsync(DeviceConfig config, CancellationToken ct)
    {
        var serverInfo = await FetchServerVersionAsync(config, ct);
        if (serverInfo is null) return;

        var running = typeof(SelfUpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        if (!Version.TryParse(serverInfo.Version, out var serverVer) || serverVer <= running)
        {
            DiagLog.Write($"SelfUpdateService: up to date at v{running}.");
            return;
        }

        DiagLog.Write($"SelfUpdateService: v{serverVer} available (running v{running}) — downloading.");
        var msiPath = await DownloadAndVerifyMsiAsync(serverInfo.MsiDownloadUrl, ct);
        if (msiPath is null) return;

        WriteTrigger($"update|{msiPath}");
        FireUpdaterTask();
        DiagLog.Write($"SelfUpdateService: update trigger written; SYSTEM task fired for v{serverVer}.");
    }

    // ─── Remote uninstall ───────────────────────────────────────────────────

    /// <summary>
    /// Called from the hub "UninstallAgent" handler. Restores the lock screen,
    /// writes an uninstall trigger, and fires the SYSTEM updater task. The task
    /// runs msiexec /x which kills this process and removes the product.
    /// </summary>
    public static async Task RequestUninstallAsync(CancellationToken ct)
    {
        DiagLog.Write("SelfUpdateService: remote uninstall requested.");

        // Restore the original lock screen before the product is removed.
        try
        {
            await LockScreenService.ApplyAsync(null, ct);
            DiagLog.Write("SelfUpdateService: lock screen restored.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: lock screen restore failed (non-fatal): {ex.GetType().Name}: {ex.Message}");
        }

        var productCode = ReadInstalledProductCode();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            DiagLog.Write("SelfUpdateService: no InstalledProductCode in registry — cannot trigger MSI uninstall. Agent will exit without removing software.");
            return;
        }

        WriteTrigger($"uninstall|{productCode}");
        FireUpdaterTask();
        DiagLog.Write($"SelfUpdateService: uninstall trigger written for product {productCode}; SYSTEM task fired.");
    }

    // ─── Updater task mode (--run-updater, runs as SYSTEM) ──────────────────

    /// <summary>
    /// Entry point for the SYSTEM-level updater task. Reads the trigger file,
    /// executes the appropriate msiexec command, and exits. Called from the
    /// ToastNotificationUpdater scheduled task via "--run-updater" arg.
    ///
    /// Runs as SYSTEM — no tray, no registration, no hub. Short-lived launcher only.
    /// </summary>
    public static int RunUpdaterMode()
    {
        DiagLog.Write("UpdaterMode: SYSTEM task started.");

        var triggerPath = Path.Combine(GetProgramDataDir(), TriggerFileName);
        if (!File.Exists(triggerPath))
        {
            DiagLog.Write("UpdaterMode: no trigger file — nothing to do.");
            return 0;
        }

        string trigger;
        try
        {
            trigger = File.ReadAllText(triggerPath, System.Text.Encoding.UTF8).Trim();
            File.Delete(triggerPath);
            DiagLog.Write($"UpdaterMode: trigger='{trigger}'.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdaterMode: trigger file read/delete failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        var sep = trigger.IndexOf('|');
        if (sep < 1)
        {
            DiagLog.Write($"UpdaterMode: malformed trigger '{trigger}'.");
            return 1;
        }

        var action = trigger[..sep];
        var arg    = trigger[(sep + 1)..];

        return action switch
        {
            "update"    => ExecuteMsiexec($"/i \"{arg}\" /qn /norestart"),
            "uninstall" => ExecuteMsiexec($"/x \"{arg}\" /qn /norestart"),
            _           => LogAndReturn($"UpdaterMode: unknown action '{action}'.", 1),
        };
    }

    private static int ExecuteMsiexec(string args)
    {
        DiagLog.Write($"UpdaterMode: launching msiexec {args}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "msiexec.exe",
                Arguments       = args,
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            // Launch and detach — msiexec will kill this process (KillAgent CA) then
            // install/remove files. We don't wait; the task scheduler records the exit
            // code of this launcher process (0 = launched successfully), not msiexec.
            System.Diagnostics.Process.Start(psi);
            DiagLog.Write("UpdaterMode: msiexec launched.");
            return 0;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdaterMode: msiexec launch failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task<AgentVersionResponse?> FetchServerVersionAsync(DeviceConfig config, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(config.ServerUrl) };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ToastNotificationAgent", ThisAssembly.Version));
        try
        {
            using var resp = await http.GetAsync("/api/agent/version", ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"SelfUpdateService: version endpoint returned {(int)resp.StatusCode}.");
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<AgentVersionResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: version fetch failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> DownloadAndVerifyMsiAsync(string downloadUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
        {
            DiagLog.Write($"SelfUpdateService: invalid MSI download URL '{downloadUrl}'.");
            return null;
        }

        var updateDir = Path.Combine(GetProgramDataDir(), UpdateSubDir);
        Directory.CreateDirectory(updateDir);
        var msiPath = Path.Combine(updateDir, UpdatedMsiName);

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ToastNotificationAgent", ThisAssembly.Version));

            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            if (resp.Content.Headers.ContentLength > MaxMsiBytes)
            {
                DiagLog.Write($"SelfUpdateService: MSI Content-Length {resp.Content.Headers.ContentLength} exceeds {MaxMsiBytes} — rejected.");
                return null;
            }

            await using var src  = await resp.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(msiPath + ".tmp");
            var buffer = new byte[81920];
            long total = 0;
            int  read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxMsiBytes)
                {
                    DiagLog.Write($"SelfUpdateService: MSI stream exceeded {MaxMsiBytes} bytes — rejected.");
                    return null;
                }
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            dest.Close();
            File.Move(msiPath + ".tmp", msiPath, overwrite: true);
            DiagLog.Write($"SelfUpdateService: MSI downloaded ({total:N0} bytes) to '{msiPath}'.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: MSI download failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (!IsSignedByToast2IT(msiPath))
        {
            DiagLog.Write("SelfUpdateService: MSI failed Authenticode verification — rejected.");
            try { File.Delete(msiPath); } catch { /* best-effort */ }
            return null;
        }

        DiagLog.Write("SelfUpdateService: MSI Authenticode verified.");
        return msiPath;
    }

    private static void WriteTrigger(string content)
    {
        var dir  = GetProgramDataDir();
        var path = Path.Combine(dir, TriggerFileName);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        DiagLog.Write($"SelfUpdateService: trigger written to '{path}'.");
    }

    private static void FireUpdaterTask()
    {
        var schtasks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = schtasks,
                Arguments       = $"/Run /TN \"{UpdaterTaskName}\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            System.Diagnostics.Process.Start(psi);
            DiagLog.Write("SelfUpdateService: updater task fired.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: failed to fire updater task: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsAutoUpdateEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue("DisableAutoUpdate") is int v && v != 0) return false;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: registry read error: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }

    private static string? ReadInstalledProductCode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            return key?.GetValue("InstalledProductCode") as string;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: ProductCode registry read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string GetProgramDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Toast2IT", "Toast Notification");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int LogAndReturn(string msg, int code)
    {
        DiagLog.Write(msg);
        return code;
    }

    // ─── Authenticode verification (mirrors UpdateService.IsSignedByToast2IT) ─

    private static bool IsSignedByToast2IT(string filePath)
    {
        try
        {
            using var cert = X509Certificate2.CreateFromSignedFile(filePath);
            if (!cert.Subject.Contains(ExpectedSubject, StringComparison.OrdinalIgnoreCase))
            {
                DiagLog.Write($"SelfUpdateService: cert subject mismatch — expected '{ExpectedSubject}', got '{cert.Subject}'.");
                return false;
            }
            return WinVerifyTrustResult(filePath) == 0;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: Authenticode check failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static uint WinVerifyTrustResult(string filePath)
    {
        var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct      = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile         = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(fileInfo, filePtr, false);
        var trustData = new WINTRUST_DATA
        {
            cbStruct            = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData      = IntPtr.Zero,
            dwUIChoice          = 2,
            fdwRevocationChecks = 0,
            dwUnionChoice       = 1,
            pFile               = filePtr,
            dwStateAction       = 1,
            hWVTStateData       = IntPtr.Zero,
            pwszURLReference    = null,
            dwUIContext         = 0,
        };
        var trustPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        Marshal.StructureToPtr(trustData, trustPtr, false);
        uint result;
        try
        {
            result = WinVerifyTrust(IntPtr.Zero, ref actionId, trustPtr);
            trustData.dwStateAction = 2;
            Marshal.StructureToPtr(trustData, trustPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref actionId, trustPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(trustPtr);
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

    private record AgentVersionResponse(string Version, string MsiDownloadUrl);
}
