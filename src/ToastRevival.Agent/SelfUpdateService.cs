using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using ToastRevival.Agent.Core;

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
    private const string ApplyTaskName   = @"\Toast2IT\ToastNotificationApplyUpdate";
    private const string TriggerFileName = "pending-action.txt";
    private const string MsiActionLogName = "msi-install.log";
    private const string UpdateSubDir    = "update";    // user-writable staging (download lands here)
    private const string VerifiedSubDir  = "verified";  // SYSTEM/Admin-only; verify+install happen here
    private const string UpdatedMsiName  = "ToastNotification.Agent.msi";
    private const string ExpectedSubject = "Toast2IT, LLC";
    private const long   MaxMsiBytes     = 200 * 1024 * 1024; // 200 MB ceiling
    private const ulong  BitsSizeUnknown = ulong.MaxValue;

    // ─── MSI update loop ────────────────────────────────────────────────────

    /// <summary>
    /// Polls /api/agent/version once at startup then every 24h. No-op for
    /// Velopack-managed installs (they use UpdateService). Respects
    /// DisableAutoUpdate registry toggle.
    /// </summary>
    public static async Task RunMsiUpdateLoopAsync(DeviceConfig config, CancellationToken ct)
    {
        if (ShouldSkipMsiUpdates(out var skipReason))
        {
            DiagLog.Write($"SelfUpdateService: MSI update loop skipped — {skipReason}.");
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

    /// <summary>
    /// Admin-triggered immediate update check, invoked from the "CheckForUpdate"
    /// hub command so the fleet can be rolled forward without waiting for the 24h
    /// poll. Same guards as the periodic loop: no-op for Velopack-managed installs
    /// and when DisableAutoUpdate is set.
    /// </summary>
    public static async Task ForceCheckAsync(DeviceConfig config, CancellationToken ct)
    {
        if (ShouldSkipMsiUpdates(out var skipReason))
        {
            DiagLog.Write($"SelfUpdateService: ForceCheck ignored — {skipReason}.");
            return;
        }
        DiagLog.Write("SelfUpdateService: admin-triggered update check starting.");
        await CheckAndTriggerAsync(config, ct);
    }

    // Serializes update checks so an admin-pushed ForceCheck cannot race the
    // periodic loop into a double download / double updater-task fire.
    private static readonly SemaphoreSlim _checkGate = new(1, 1);

    private static async Task CheckAndTriggerAsync(DeviceConfig config, CancellationToken ct)
    {
        if (!await _checkGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            DiagLog.Write("SelfUpdateService: update check already in progress — skipping duplicate.");
            return;
        }
        try { await CheckAndTriggerCoreAsync(config, ct); }
        finally { _checkGate.Release(); }
    }

    private static async Task CheckAndTriggerCoreAsync(DeviceConfig config, CancellationToken ct)
    {
        var serverInfo = await FetchServerVersionAsync(config, ct);
        if (serverInfo is null) return;

        var running = typeof(SelfUpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        if (!UpdateDecision.TryGetNewerServerVersion(serverInfo.Version, running, out var serverVer))
        {
            DiagLog.Write($"SelfUpdateService: up to date at v{running}.");
            return;
        }

        DiagLog.Write($"SelfUpdateService: v{serverVer} available (running v{running}) — downloading.");
        var msiPath = await DownloadAndVerifyMsiAsync(serverInfo.MsiDownloadUrl, ct);
        if (msiPath is null) return;

        WriteTrigger($"update|{msiPath}");
        var fired = FireUpdaterTask();
        DiagLog.Write(fired
            ? $"SelfUpdateService: update trigger written; SYSTEM task fired for v{serverVer}."
            : $"SelfUpdateService: update trigger written for v{serverVer} but updater task launch FAILED — update will not apply until task is repaired.");
    }

    // ─── Remote uninstall ───────────────────────────────────────────────────

    /// <summary>
    /// Called from the hub "UninstallAgent" handler. Restores the lock screen,
    /// writes an uninstall trigger, and fires the SYSTEM updater task. The task
    /// runs msiexec /x which kills this process and removes the product.
    /// Returns true if the SYSTEM updater task fired (so the caller can ack the
    /// server); false if no product code was found or the task did not launch.
    /// </summary>
    public static async Task<bool> RequestUninstallAsync(CancellationToken ct)
    {
        DiagLog.Write("SelfUpdateService: remote uninstall requested.");

        var productCode = ReadInstalledProductCode();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            DiagLog.Write("SelfUpdateService: no InstalledProductCode in registry — cannot trigger MSI uninstall. Agent will exit without removing software.");
            return false;
        }

        // Write trigger and fire SYSTEM task first — must succeed before process exits.
        // Lock screen revert is best-effort and runs after the trigger is committed so
        // a slow revert or an early process exit cannot silently drop the uninstall.
        WriteTrigger($"uninstall|{productCode}");
        var fired = FireUpdaterTask();
        DiagLog.Write(fired
            ? $"SelfUpdateService: uninstall trigger written for product {productCode}; SYSTEM task fired."
            : $"SelfUpdateService: uninstall trigger written for product {productCode} but updater task launch FAILED — uninstall will not execute until task is repaired.");

        try
        {
            await LockScreenService.RevertAsync(ct);
            DiagLog.Write("SelfUpdateService: lock screen restored.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: lock screen restore failed (non-fatal): {ex.GetType().Name}: {ex.Message}");
        }

        // Returned to the hub handler so it only sends UninstallAck when the SYSTEM
        // uninstall task actually fired (REL-004-R / CR-P0-006 follow-on).
        return fired;
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

        // REVIEW Agent-L2 (2026-05-31): trigger file lives in user-writable ProgramData and
        // can be written by a local non-admin — that is safe by design. The uninstall arg is
        // GUID-validated (s_productCodeRx) and the update arg is a path whose bytes are copied
        // into the SYSTEM/Admin-only verified\ dir and Authenticode-re-verified before execution,
        // so a forged trigger can at most point at an unsigned MSI, which fails the signature gate.
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
            "update"    => ExecuteVerifiedMsiUpdate(arg),
            "uninstall" => ExecuteVerifiedMsiUninstall(arg),
            _           => LogAndReturn($"UpdaterMode: unknown action '{action}'.", 1),
        };
    }

    // Runs "msiexec <args>" from a standalone one-shot SYSTEM scheduled task instead
    // of as a child of this updater process. LOAD-BEARING — do not "simplify" back to
    // a direct Process.Start. When msiexec was a child of agent.exe --run-updater
    // (itself launched by the ToastNotificationUpdater task) it lived inside that
    // task's job object AND the agent's process tree, and TWO things killed it
    // mid-install: (1) the MSI's KillAgent action — taskkill /F /IM
    // ToastNotification.Agent.exe /T — tree-kills it, and (2) Task Scheduler tears
    // down the updater task's job the instant --run-updater returns. Either way the
    // over-the-top upgrade rolled back and the agent stayed on the old version
    // (observed in the field: 0.4.35 looping on 0.4.36, a stuck msiexec holding the
    // Windows Installer mutex). A separate scheduled task runs msiexec as its OWN task
    // process, in its OWN job, owned by neither the agent nor the updater task — so
    // KillAgent cannot reach it and the updater task exiting cannot kill it, and it
    // runs to completion. /l*v writes a verbose install log so the result is never
    // invisible again.
    private static int ExecuteMsiexec(string args)
    {
        try
        {
            var verifiedDir = EnsureProtectedVerifiedDir();
            var logPath = Path.Combine(GetProgramDataDir(), MsiActionLogName);
            var cmdPath = Path.Combine(verifiedDir, "apply-msi.cmd");

            // The .cmd lives in the SYSTEM/Administrators-only verified dir so a
            // non-admin cannot alter what SYSTEM is about to execute. Remove any
            // pre-existing entry first (link-safe) so we can't be tricked into writing
            // through a symlink a non-admin planted before the dir was locked.
            RemoveExistingEntry(cmdPath);
            File.WriteAllText(
                cmdPath,
                "@echo off\r\n" + $"msiexec.exe {args} /l*v \"{logPath}\"\r\n",
                new System.Text.UTF8Encoding(false));

            DiagLog.Write($"UpdaterMode: scheduling msiexec via standalone task; args='{args}'.");

            // Best-effort cleanup of any prior instance, then (re)create and run.
            RunSchtasks("/delete", "/tn", ApplyTaskName, "/f");
            if (RunSchtasks("/create", "/tn", ApplyTaskName,
                            "/tr", $"cmd.exe /c \"{cmdPath}\"",
                            "/sc", "ONCE", "/st", "00:00",
                            "/ru", "SYSTEM", "/rl", "HIGHEST", "/f") != 0)
                return 1;
            if (RunSchtasks("/run", "/tn", ApplyTaskName) != 0)
                return 1;

            DiagLog.Write($"UpdaterMode: msiexec apply task started (independent of updater task); install log -> {logPath}.");
            return 0;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdaterMode: failed to schedule msiexec: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // Invokes schtasks.exe with the given args (via ArgumentList so paths with spaces
    // and embedded quotes are passed correctly). Returns the exit code; logs non-zero.
    private static int RunSchtasks(params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            // AGENT-L3: drain stdout and stderr concurrently so a full stderr pipe buffer
            // can't deadlock a blocking ReadToEnd(stdout), and bound the wait so a hung
            // schtasks can't pin the updater path forever (mirrors FireUpdaterTask's 10s guard).
            var soTask = p.StandardOutput.ReadToEndAsync();
            var seTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(10_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                DiagLog.Write($"UpdaterMode: schtasks {(args.Length > 0 ? args[0] : "")} timed out after 10s; killed.");
                return 1;
            }
            var so = soTask.GetAwaiter().GetResult();
            var se = seTask.GetAwaiter().GetResult();
            if (p.ExitCode != 0)
                DiagLog.Write($"UpdaterMode: schtasks {args[0]} exit {p.ExitCode}: {(so + se).Trim()}");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdaterMode: schtasks {(args.Length > 0 ? args[0] : "")} launch failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // FIX-Agent-H1 (2026-05-30): Closes the verify->use TOCTOU on the staged MSI.
    // The user-context agent stages the MSI under %ProgramData%\...\update\, a
    // directory the interactive user owns and can overwrite. Re-verifying that
    // user-writable path and then launching msiexec against it is two separate
    // opens — a local non-admin can swap the file in between and gain SYSTEM
    // execution. The SYSTEM updater therefore COPIES the staged MSI into a
    // SYSTEM/Administrators-only directory FIRST, then verifies Authenticode and
    // runs msiexec on that protected copy. Once the bytes live where the
    // unprivileged user cannot touch them, verify-time and use-time content are
    // provably identical and the race is eliminated, not merely narrowed.
    private static int ExecuteVerifiedMsiUpdate(string stagedMsiPath)
    {
        if (!File.Exists(stagedMsiPath))
        {
            DiagLog.Write($"UpdaterMode: MSI path not found: '{stagedMsiPath}'.");
            return 1;
        }

        string verifiedMsiPath;
        try
        {
            verifiedMsiPath = CopyToProtectedDir(stagedMsiPath);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"UpdaterMode: could not stage MSI into protected dir: {ex.GetType().Name}: {ex.Message} — msiexec aborted.");
            return 1;
        }

        // Verify the PROTECTED copy, then install the SAME path. The user cannot
        // write into the protected dir, so nothing can swap it between these calls.
        if (!IsSignedByToast2IT(verifiedMsiPath))
        {
            DiagLog.Write("UpdaterMode: SYSTEM-side Authenticode re-verification failed — msiexec aborted.");
            try { File.Delete(verifiedMsiPath); } catch { /* best-effort */ }
            try { File.Delete(stagedMsiPath); } catch { /* best-effort */ }
            return 1;
        }
        DiagLog.Write("UpdaterMode: SYSTEM-side Authenticode verified on protected copy.");
        return ExecuteMsiexec($"/i \"{verifiedMsiPath}\" /qn /norestart");
    }

    // Copies the staged MSI into a SYSTEM/Administrators-only subdirectory and
    // returns the protected path. The directory ACL is reset to grant Full Control
    // only to SYSTEM and the local Administrators group (inheritance disabled), so
    // the interactive user cannot modify the file after this copy. Runs as SYSTEM.
    private static string CopyToProtectedDir(string stagedMsiPath)
    {
        var verifiedDir = EnsureProtectedVerifiedDir();
        var verifiedMsiPath = Path.Combine(verifiedDir, UpdatedMsiName);
        // Remove any pre-existing entry (link-safe) before copying so we cannot be
        // tricked into writing through a planted symlink.
        RemoveExistingEntry(verifiedMsiPath);
        File.Copy(stagedMsiPath, verifiedMsiPath, overwrite: true);
        // The user-staged copy has served its purpose; remove it best-effort.
        try { File.Delete(stagedMsiPath); } catch { /* best-effort */ }
        return verifiedMsiPath;
    }

    // Creates (or re-creates) the SYSTEM/Administrators-only `verified` directory and
    // returns its path. The parent dir is user-writable (the user-context agent stages
    // downloads there), so before our first run a non-admin could pre-create `verified`
    // as a junction/symlink to redirect SYSTEM's writes to an arbitrary location. We
    // refuse to operate through any reparse point, recreate the dir fresh so it is
    // SYSTEM-owned, lock its ACL to SYSTEM + Administrators, then re-check it is still a
    // real directory. Everything SYSTEM later writes here — the verified MSI and the
    // apply-msi.cmd — is therefore tamper-proof from non-admins. Runs as SYSTEM.
    private static string EnsureProtectedVerifiedDir()
    {
        var verifiedDir = Path.Combine(GetProgramDataDir(), VerifiedSubDir);
        var existing = new DirectoryInfo(verifiedDir);
        if (existing.Exists)
        {
            // Remove ANY pre-existing entry so SYSTEM is unambiguously the creator of the
            // dir we lock down below. A reparse point is unlinked WITHOUT following it to
            // its target; a plain directory is deleted wholesale. The plain-dir case is the
            // attack this closes: the parent is user-writable by design, so a non-admin can
            // pre-create `verified` as an ordinary directory and stay its OWNER. CreateDirectory
            // no-ops on an existing dir, so without this delete that owner survives, keeps
            // implicit WRITE_DAC, and could rewrite our ACL to swap the SYSTEM-executed
            // apply-msi.cmd (which, unlike the MSI, is not Authenticode-gated).
            if ((existing.Attributes & FileAttributes.ReparsePoint) != 0)
                existing.Delete(recursive: false); // removes the link, never its target
            else
                existing.Delete(recursive: true);  // real dir (possibly non-admin-owned) -> gone
        }
        Directory.CreateDirectory(verifiedDir);
        LockDownToSystemAndAdmins(verifiedDir);
        var locked = new DirectoryInfo(verifiedDir);
        if ((locked.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to use reparse-point dir '{verifiedDir}'.");
        return verifiedDir;
    }

    // REVIEW Agent-L1 (2026-05-31): ACL set in C# here rather than in WiX is intentional.
    // The verified\ dir is unconditionally recreated, SetOwner'd to SYSTEM, and re-locked
    // + reparse-checked every run before use (see EnsureProtectedVerifiedDir +
    // LockDownToSystemAndAdmins), so SYSTEM is the sole owner at use-time and a
    // WiX-pre-seeded ACL would not be load-bearing. Resolved no-change.
    // Agent-M1 (2026-06-03): the prior "owned exclusively by the SYSTEM updater" premise is
    // now ENFORCED in code — a non-admin pre-created plain dir is deleted wholesale and
    // ownership is forced to SYSTEM — closing the local->SYSTEM owner-rights (WRITE_DAC) gap
    // and resolving the Agent-L1 anchor-challenge.
    // Deletes a file or directory entry if present, removing a SYMLINK itself rather
    // than following it to its target (File.Delete / Directory.Delete on a reparse
    // point unlinks the link only). No-op if the path does not exist. Used to clear a
    // pre-existing entry from the locked verified dir before SYSTEM writes over it,
    // closing the symlink-follow TOCTOU on the apply .cmd and the verified MSI.
    private static void RemoveExistingEntry(string path)
    {
        FileAttributes attrs;
        try { attrs = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attrs & FileAttributes.Directory) != 0)
            Directory.Delete(path, recursive: false); // empty dir or dir-symlink (link only)
        else
            File.Delete(path);                          // file or file-symlink (link only)
    }

    private static void LockDownToSystemAndAdmins(string dir)
    {
        var di  = new DirectoryInfo(dir);
        var sec = new DirectorySecurity();
        // Disable inheritance and drop any inherited ACEs — start from a clean slate.
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        // Force ownership to SYSTEM. An object's OWNER always holds implicit WRITE_DAC
        // ("owner rights") and can rewrite the DACL regardless of the ACEs below, so if a
        // non-admin pre-created this dir they would otherwise remain owner even after we
        // set the ACL — and could swap apply-msi.cmd. We run as SYSTEM (which holds
        // SeRestorePrivilege), so assigning SYSTEM as owner is permitted. Belt-and-suspenders
        // with the unconditional recreate in EnsureProtectedVerifiedDir.
        sec.SetOwner(system);
        foreach (var sid in new[] { system, admins })
        {
            sec.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        di.SetAccessControl(sec);
    }

    // ProductCode must be a well-formed GUID so the trigger arg cannot be an
    // arbitrary executable path injected via a tampered trigger file.
    private static readonly Regex s_productCodeRx = new(
        @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
        RegexOptions.Compiled);

    private static int ExecuteVerifiedMsiUninstall(string productCode)
    {
        if (!s_productCodeRx.IsMatch(productCode))
        {
            DiagLog.Write($"UpdaterMode: uninstall arg is not a valid ProductCode GUID — msiexec aborted.");
            return 1;
        }
        return ExecuteMsiexec($"/x \"{productCode}\" /qn /norestart");
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
#if !DEBUG
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https")
#else
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
#endif
        {
            DiagLog.Write($"SelfUpdateService: invalid MSI download URL '{downloadUrl}' (https required).");
            return null;
        }

        var updateDir = Path.Combine(GetProgramDataDir(), UpdateSubDir);
        Directory.CreateDirectory(updateDir);
        var msiPath = Path.Combine(updateDir, UpdatedMsiName);
        var tmpPath = msiPath + ".tmp";

        try
        {
            TryDeleteFile(tmpPath);
            var bitsResult = await DownloadFileWithBitsAsync(uri, tmpPath, ct).ConfigureAwait(false);
            var downloadedBytes = new FileInfo(tmpPath).Length;
            if (downloadedBytes <= 0 || downloadedBytes > MaxMsiBytes)
            {
                DiagLog.Write($"SelfUpdateService: MSI BITS download size {downloadedBytes:N0} invalid or exceeds {MaxMsiBytes} — rejected.");
                TryDeleteFile(tmpPath);
                return null;
            }

            File.Move(tmpPath, msiPath, overwrite: true);
            DiagLog.Write($"SelfUpdateService: MSI downloaded via BITS ({downloadedBytes:N0} bytes, job {bitsResult.JobId}) to '{msiPath}'.");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: MSI download failed: {ex.GetType().Name}: {ex.Message}");
            TryDeleteFile(tmpPath);
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

    private static async Task<BitsDownloadResult> DownloadFileWithBitsAsync(Uri source, string destination, CancellationToken ct)
    {
        object? managerObject = null;
        IBackgroundCopyJob? job = null;
        var jobId = Guid.Empty;
        var completed = false;

        try
        {
            managerObject = new BackgroundCopyManager();
            var manager = (IBackgroundCopyManager)managerObject;
            manager.CreateJob("Toast Notification MSI self-update", BG_JOB_TYPE.BG_JOB_TYPE_DOWNLOAD, out jobId, out job);
            job.SetDescription("Downloads a signed Toast Notification agent MSI update.");
            job.SetPriority(BG_JOB_PRIORITY.BG_JOB_PRIORITY_FOREGROUND);
            job.SetMinimumRetryDelay(30);
            job.SetNoProgressTimeout(300);
            job.AddFile(source.AbsoluteUri, destination);
            job.Resume();

            DiagLog.Write($"SelfUpdateService: BITS MSI download job {jobId} started.");

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                job.GetProgress(out var progress);
                if (progress.BytesTotal != BitsSizeUnknown && progress.BytesTotal > (ulong)MaxMsiBytes)
                    throw new InvalidOperationException($"BITS job {jobId} reports MSI size {progress.BytesTotal:N0}, exceeding {MaxMsiBytes}.");
                if (progress.BytesTransferred > (ulong)MaxMsiBytes)
                    throw new InvalidOperationException($"BITS job {jobId} exceeded MSI size cap {MaxMsiBytes}.");

                job.GetState(out var state);
                switch (state)
                {
                    case BG_JOB_STATE.BG_JOB_STATE_TRANSFERRED:
                        job.Complete();
                        completed = true;
                        return new BitsDownloadResult(jobId, progress.BytesTransferred);

                    case BG_JOB_STATE.BG_JOB_STATE_ERROR:
                        throw new InvalidOperationException($"BITS job {jobId} entered ERROR state.");

                    case BG_JOB_STATE.BG_JOB_STATE_CANCELLED:
                        throw new InvalidOperationException($"BITS job {jobId} was cancelled.");

                    case BG_JOB_STATE.BG_JOB_STATE_ACKNOWLEDGED:
                        throw new InvalidOperationException($"BITS job {jobId} was already acknowledged before completion.");

                    case BG_JOB_STATE.BG_JOB_STATE_TRANSIENT_ERROR:
                        job.Resume();
                        break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!completed && job is not null)
            {
                try { job.Cancel(); } catch { /* best-effort BITS cleanup */ }
            }

            ReleaseComObject(job);
            ReleaseComObject(managerObject);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort cleanup */ }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
            Marshal.FinalReleaseComObject(obj);
    }

    private static void WriteTrigger(string content)
    {
        var dir  = GetProgramDataDir();
        var path = Path.Combine(dir, TriggerFileName);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        DiagLog.Write($"SelfUpdateService: trigger written to '{path}'.");
    }

    // REL-007-R: wait for schtasks.exe to exit and capture its output so a missing
    // or disabled task surfaces as a logged failure rather than silent "task fired."
    // Returns true when schtasks reports the task was successfully queued (exit 0);
    // false on non-zero exit, timeout, or the task not existing at all.
    private static bool FireUpdaterTask()
    {
        var schtasks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = schtasks,
                Arguments              = $"/Run /TN \"{UpdaterTaskName}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                DiagLog.Write("SelfUpdateService: schtasks.exe could not be started.");
                return false;
            }

            bool exited = proc.WaitForExit(10_000); // 10s — task trigger is near-instant
            var stdout = proc.StandardOutput.ReadToEnd().Trim();
            var stderr = proc.StandardError.ReadToEnd().Trim();

            if (!exited)
            {
                DiagLog.Write("SelfUpdateService: schtasks.exe timed out waiting for exit.");
                try { proc.Kill(); } catch { /* best-effort */ }
                return false;
            }

            if (proc.ExitCode != 0)
            {
                DiagLog.Write($"SelfUpdateService: schtasks /Run exited {proc.ExitCode}. stdout={stdout} stderr={stderr}");
                return false;
            }

            DiagLog.Write($"SelfUpdateService: updater task fired (exit 0). stdout={stdout}");
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: failed to fire updater task: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // True when MSI self-update should not run for this install: either Velopack
    // owns it (it has its own updater) or the admin set DisableAutoUpdate. Shared
    // by the periodic loop and the admin-triggered ForceCheck.
    private static bool ShouldSkipMsiUpdates(out string reason)
    {
        bool isVelopackManaged;
        try
        {
            var mgr = new Velopack.UpdateManager(string.Empty);
            isVelopackManaged = mgr.IsInstalled;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"SelfUpdateService: Velopack detection faulted ({ex.GetType().Name}: {ex.Message}) — assuming MSI-deployed, continuing.");
            isVelopackManaged = false;
        }
        if (isVelopackManaged) { reason = "Velopack-managed install"; return true; }
        if (!IsAutoUpdateEnabled()) { reason = "DisableAutoUpdate set"; return true; }
        reason = "";
        return false;
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

    private readonly record struct BitsDownloadResult(Guid JobId, ulong BytesTransferred);

    private enum BG_JOB_TYPE
    {
        BG_JOB_TYPE_DOWNLOAD = 0,
    }

    private enum BG_JOB_PRIORITY
    {
        BG_JOB_PRIORITY_FOREGROUND = 0,
        BG_JOB_PRIORITY_HIGH       = 1,
        BG_JOB_PRIORITY_NORMAL     = 2,
        BG_JOB_PRIORITY_LOW        = 3,
    }

    private enum BG_JOB_STATE
    {
        BG_JOB_STATE_QUEUED          = 0,
        BG_JOB_STATE_CONNECTING      = 1,
        BG_JOB_STATE_TRANSFERRING    = 2,
        BG_JOB_STATE_SUSPENDED       = 3,
        BG_JOB_STATE_ERROR           = 4,
        BG_JOB_STATE_TRANSIENT_ERROR = 5,
        BG_JOB_STATE_TRANSFERRED     = 6,
        BG_JOB_STATE_ACKNOWLEDGED    = 7,
        BG_JOB_STATE_CANCELLED       = 8,
    }

    private enum BG_JOB_PROXY_USAGE
    {
        BG_JOB_PROXY_USAGE_PRECONFIG   = 0,
        BG_JOB_PROXY_USAGE_NO_PROXY    = 1,
        BG_JOB_PROXY_USAGE_OVERRIDE    = 2,
        BG_JOB_PROXY_USAGE_AUTODETECT  = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BG_JOB_PROGRESS
    {
        public ulong BytesTotal;
        public ulong BytesTransferred;
        public uint  FilesTotal;
        public uint  FilesTransferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BG_JOB_TIMES
    {
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ModificationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME TransferCompletionTime;
    }

    [ComImport]
    [Guid("4991D34B-80A1-4291-83B6-3328366B9097")]
    private class BackgroundCopyManager
    {
    }

    [ComImport]
    [Guid("5CE34C0D-0DC9-4C1F-897C-DAA1B78CEE7C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBackgroundCopyManager
    {
        void CreateJob(
            [MarshalAs(UnmanagedType.LPWStr)] string displayName,
            BG_JOB_TYPE type,
            out Guid jobId,
            out IBackgroundCopyJob job);

        void GetJob(ref Guid jobId, out IBackgroundCopyJob job);
        void EnumJobs(uint flags, out IntPtr enumJobs);
        void GetErrorDescription(int hResult, uint languageId, [MarshalAs(UnmanagedType.LPWStr)] out string errorDescription);
    }

    [ComImport]
    [Guid("37668D37-507E-4160-9316-26306D150B12")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBackgroundCopyJob
    {
        void AddFileSet(uint fileCount, IntPtr fileSet);
        void AddFile([MarshalAs(UnmanagedType.LPWStr)] string remoteUrl, [MarshalAs(UnmanagedType.LPWStr)] string localName);
        void EnumFiles(out IntPtr enumFiles);
        void Suspend();
        void Resume();
        void Cancel();
        void Complete();
        void GetId(out Guid id);
        void GetType(out BG_JOB_TYPE type);
        void GetProgress(out BG_JOB_PROGRESS progress);
        void GetTimes(out BG_JOB_TIMES times);
        void GetState(out BG_JOB_STATE state);
        void GetError([MarshalAs(UnmanagedType.Interface)] out object error);
        void GetOwner([MarshalAs(UnmanagedType.LPWStr)] out string owner);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] out string description);
        void SetPriority(BG_JOB_PRIORITY priority);
        void GetPriority(out BG_JOB_PRIORITY priority);
        void SetNotifyFlags(uint flags);
        void GetNotifyFlags(out uint flags);
        void SetNotifyInterface([MarshalAs(UnmanagedType.IUnknown)] object notifyInterface);
        void GetNotifyInterface([MarshalAs(UnmanagedType.IUnknown)] out object notifyInterface);
        void SetMinimumRetryDelay(uint seconds);
        void GetMinimumRetryDelay(out uint seconds);
        void SetNoProgressTimeout(uint seconds);
        void GetNoProgressTimeout(out uint seconds);
        void GetErrorCount(out uint errors);
        void SetProxySettings(
            BG_JOB_PROXY_USAGE proxyUsage,
            [MarshalAs(UnmanagedType.LPWStr)] string? proxyList,
            [MarshalAs(UnmanagedType.LPWStr)] string? proxyBypassList);
        void GetProxySettings(
            out BG_JOB_PROXY_USAGE proxyUsage,
            [MarshalAs(UnmanagedType.LPWStr)] out string proxyList,
            [MarshalAs(UnmanagedType.LPWStr)] out string proxyBypassList);
        void TakeOwnership();
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

    private const uint WTD_REVOKE_NONE       = 0;
    private const uint WTD_REVOKE_WHOLECHAIN = 0x00000002;

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
            fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN, // WSEC-H1: check full chain revocation
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
