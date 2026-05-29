using System.Security.Principal;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using ToastRevival.Agent;
using Velopack;

// Velopack startup hook — MUST be first, before any other app init.
// Handles --velopack-install / --velopack-updated / --velopack-uninstall lifecycle args.
// When none of those args are present (normal launch) this is a no-op.
// Safe to call in --setup-bootstrap (SYSTEM) context: no --velopack-* args → immediate return.
VelopackApp.Build()
    .OnAfterInstallFastCallback(v => DiagLog.Write($"Velopack: installed v{v}"))
    .OnAfterUpdateFastCallback(v  => DiagLog.Write($"Velopack: updated to v{v}"))
    .Run();

return await AgentEntryPoint.RunAsync(args);

namespace ToastRevival.Agent
{
    /// <summary>
    /// Three-mode entry point:
    ///
    ///  1. Activation mode:   args contain the framework sentinel "----AppNotificationActivated:"
    ///                        — Windows launched us to deliver a button-click event from a toast
    ///                        whose original sender (the primary worker) was dead. Short-lived,
    ///                        skips the mutex, posts the interaction via REST, exits clean.
    ///
    ///  2. Diagnostic log dump: args contain --diag — prints the log file path and last 200 lines
    ///                        to stdout for remote support. Works before the elevation gate.
    ///
    ///  3. Diagnostic mode:   args contain --template — legacy M0A behavior. Used by the MSI
    ///                        Scheduled Task channel and for direct lab/dev launches. Renders a
    ///                        single hardcoded template and exits.
    ///
    ///  3. Primary worker:    no special args — load config (or run first-run registration),
    ///                        hold the named mutex, connect to the hub, run forever.
    ///
    /// Mutex is taken AFTER activation/diagnostic dispatch so those short-lived modes can run
    /// while the primary worker is alive (the COM activator framework is unaware of the mutex).
    /// </summary>
    internal static class AgentEntryPoint
    {
        private const string ActivationArgPrefix = "----AppNotificationActivated:";

        // Session-local mutex (NOT Global\) so each interactive Windows session
        // can run its own primary worker without colliding. Multi-user endpoints
        // (Fast User Switching, RDP, Terminal Services) get one agent per session
        // — required behavior, verified at M0 D4 against the BUILTIN\Users-group
        // Scheduled Task firing for a second logged-on user.
        private const string PrimaryMutexName = @"Local\Toast2IT.ToastNotification.PrimaryWorker";

        public static async Task<int> RunAsync(string[] args)
        {
            DiagLog.Init();
            DiagLog.Write($"==> Toast Notification agent start; pid={Environment.ProcessId}; args=[{string.Join(' ', args)}]; baseDir={AppContext.BaseDirectory}; packaged={DiagLog.IsPackaged}; logPath={DiagLog.LogFilePath}");

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                Console.Error.WriteLine("Toast Notification agent requires Windows 10 2004 / build 19041 or later.");
                DiagLog.Write("EXIT 2: runtime gate IsWindowsVersionAtLeast(10,0,19041) failed.");
                return 2;
            }

            // Self-redirect: if running from %ProgramFiles% and a newer Velopack-managed
            // copy exists in %LocalAppData%, launch it and exit. This handles logons after
            // the first Velopack update where the scheduled task still points at ProgramFiles.
            if (UpdateService.TrySelfRedirect(args))
            {
                DiagLog.Write("EXIT 0: redirected to Velopack-managed copy.");
                return 0;
            }

            // Setup mode: invoked by the MSI installer (as SYSTEM) to write bootstrap.json.
            // MUST be checked before the elevation guard — SYSTEM is elevated.
            if (HasFlag(args, "--setup-bootstrap"))
            {
                return await SetupMode.RunAsync(args);
            }

            // Diagnostic gate — dump log path + recent log content to stdout for support.
            // Works before the elevation check so support staff can run it as admin.
            if (HasFlag(args, "--diag"))
            {
                return DiagMode.Run();
            }

            // Elevation note (no longer fatal). The historical "elevated processes can't show
            // toasts" rule was a UWP/Windows.UI.Notifications limitation. WinAppSDK 1.7 supports
            // elevated callers when the AUMID + COM activator are properly registered (we do
            // both — see NotificationDisplayName.Apply + Package.appxmanifest).
            //
            // The previous exit-3 gate broke Windows Server SKUs entirely: the built-in
            // Administrator account on Server has UAC disabled by default, so the scheduled
            // task at LeastPrivilege still runs with the unfiltered admin token, IsElevated()
            // returns true, and the agent quit before ever POSTing /api/devices/register —
            // which reproduces on Windows Server 2025 with 0.4.6.1.
            if (IsElevated())
            {
                DiagLog.Write("WARN: process is running elevated — toast Show() may fail; agent will still register and connect to the hub.");
            }

            // 1. Activation mode — skip the mutex, run activation handler, exit.
            if (TryFindActivationArg(args, out _))
            {
                return await ActivationMode.RunAsync(args);
            }

            // 2. Diagnostic mode — skip the mutex, run legacy single-shot template render, exit.
            if (HasFlag(args, "--template"))
            {
                return await DiagnosticMode.RunAsync(args);
            }

            // 3. Primary worker — single-instance via named mutex.
            using var mutex = new Mutex(initiallyOwned: false, name: PrimaryMutexName, out _);
            bool gotMutex;
            try
            {
                gotMutex = mutex.WaitOne(TimeSpan.Zero, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                // Previous holder crashed without releasing — we now own it.
                gotMutex = true;
            }

            if (!gotMutex)
            {
                Console.Error.WriteLine("Another Toast Notification agent instance is already running.");
                DiagLog.Write("EXIT 5: primary mutex already held — second instance exiting cleanly.");
                return 5;
            }

            try
            {
                return await PrimaryMode.RunAsync(args);
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { /* best-effort */ }
            }
        }

        private static bool TryFindActivationArg(string[] args, out string activationArg)
        {
            foreach (var a in args)
            {
                if (a is not null && a.StartsWith(ActivationArgPrefix, StringComparison.Ordinal))
                {
                    activationArg = a;
                    return true;
                }
            }
            activationArg = "";
            return false;
        }

        private static bool HasFlag(string[] args, string flag) =>
            args.Any(a => string.Equals(a, flag, StringComparison.Ordinal));

        private static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// One-shot activation handler. The framework launched us specifically to
    /// deliver a button-click event from a toast whose primary-worker sender is
    /// currently dead. Short-circuits BEFORE the SignalR client comes up — no hub
    /// spin-up, no re-render, no mutex contention with a running primary.
    /// </summary>
    internal static class ActivationMode
    {
        public static async Task<int> RunAsync(string[] _)
        {
            DiagLog.Write("ActivationMode: entering one-shot activation handler.");

            var config = ConfigStore.TryLoad();
            if (config is null)
            {
                DiagLog.Write("ActivationMode EXIT 6: no DeviceConfig — agent never registered.");
                return 6;
            }

            // The Windows App SDK delivers the original toast's argument string through
            // NotificationInvoked, NOT through args. Subscribe before Register() so we
            // catch the framework's synchronous activation callback.
            var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(AppNotificationManager _, AppNotificationActivatedEventArgs e)
            {
                DiagLog.Write($"ActivationMode: NotificationInvoked argument='{e.Argument}'");
                captured.TrySetResult(e.Argument);
            }

            AppNotificationManager.Default.NotificationInvoked += Handler;

            try
            {
                AppNotificationManager.Default.Register();
                DiagLog.Write("ActivationMode: Register() returned.");

                // Wait briefly for the activation event to surface. If it doesn't show
                // up in 5 seconds, something went wrong — log and exit clean.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                string argument;
                try
                {
                    argument = await captured.Task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    DiagLog.Write("ActivationMode EXIT 7: NotificationInvoked never fired within 5s.");
                    return 7;
                }

                var parsed = ParseArguments(argument);
                if (parsed.GetValueOrDefault("source") != "hub"
                    || !Guid.TryParse(parsed.GetValueOrDefault("notificationId"), out var notificationId))
                {
                    DiagLog.Write("ActivationMode: argument is not a hub-routed notification — nothing to report. Exiting clean.");
                    return 0;
                }

                var action = parsed.GetValueOrDefault("action") ?? "click";
                var url = parsed.GetValueOrDefault("url");

                using var postCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var ok = await InteractionFallback.PostAsync(config, notificationId, action, postCts.Token);
                ToastUrlLauncher.OpenIfAllowed(url);

                DiagLog.Write($"ActivationMode EXIT {(ok ? 0 : 8)}: posted={ok}; notificationId={notificationId}; action={action}");
                return ok ? 0 : 8;
            }
            finally
            {
                AppNotificationManager.Default.NotificationInvoked -= Handler;
                try { AppNotificationManager.Default.Unregister(); } catch { /* best-effort */ }
            }
        }

        private static Dictionary<string, string> ParseArguments(string argument)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(argument)) return dict;
            foreach (var pair in argument.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                if (idx <= 0) continue;
                dict[pair[..idx]] = pair[(idx + 1)..];
            }
            return dict;
        }
    }

    /// <summary>
    /// MSI installer hook — writes bootstrap.json next to the exe so the agent's
    /// first-run registration can pick up CLIENTID and SERVERURL from the installer
    /// properties without requiring the user to set environment variables.
    ///
    /// Called by the WriteBootstrapJson WiX custom action (Impersonate="no", runs as
    /// SYSTEM). Detected BEFORE the elevation guard so SYSTEM context doesn't hit
    /// exit-3. The only thing this mode does is write one JSON file and exit.
    /// </summary>
    internal static class SetupMode
    {
        public static Task<int> RunAsync(string[] args)
        {
            DiagLog.Write($"SetupMode: args=[{string.Join(' ', args)}]; baseDir={AppContext.BaseDirectory}");

            // Expect: --setup-bootstrap <tenantId> <serverUrl> [enrollmentKey]
            var idx = Array.IndexOf(args, "--setup-bootstrap");
            if (idx < 0 || idx + 2 >= args.Length)
            {
                DiagLog.Write("SetupMode EXIT 1: usage: --setup-bootstrap <tenantId> <serverUrl> [enrollmentKey]");
                Console.Error.WriteLine("Usage: ToastNotification.Agent --setup-bootstrap <tenantId> <serverUrl> [enrollmentKey]");
                return Task.FromResult(1);
            }

            var tenantIdStr   = args[idx + 1];
            var serverUrl     = args[idx + 2];
            var enrollmentKey = idx + 3 < args.Length ? args[idx + 3] : null;

            if (!Guid.TryParse(tenantIdStr, out var tenantId))
            {
                DiagLog.Write($"SetupMode EXIT 1: invalid tenantId '{tenantIdStr}'");
                Console.Error.WriteLine($"Invalid tenantId: {tenantIdStr}");
                return Task.FromResult(1);
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
            {
                DiagLog.Write($"SetupMode EXIT 1: invalid serverUrl '{serverUrl}'");
                Console.Error.WriteLine($"Invalid serverUrl: {serverUrl}");
                return Task.FromResult(1);
            }

#if !DEBUG
            if (serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                DiagLog.Write($"SetupMode EXIT 1: serverUrl '{serverUrl}' uses HTTP — HTTPS is required.");
                Console.Error.WriteLine($"ServerUrl must use HTTPS: {serverUrl}");
                return Task.FromResult(1);
            }
#endif

            // Treat an empty string enrollment key (WiX passes empty string when
            // ENROLLMENTKEY property is not set) as absent.
            if (string.IsNullOrWhiteSpace(enrollmentKey)) enrollmentKey = null;

            var bootstrap = new BootstrapConfig(tenantId, serverUrl, enrollmentKey);
            var options   = new JsonSerializerOptions { WriteIndented = true };
            var path      = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");

            try
            {
                var json = JsonSerializer.Serialize(bootstrap, options);
                var temp = path + ".tmp";
                File.WriteAllText(temp, json, System.Text.Encoding.UTF8);
                File.Move(temp, path, overwrite: true);
                DiagLog.Write($"SetupMode EXIT 0: wrote bootstrap.json at '{path}'; tenantId={tenantId}; serverUrl={serverUrl}");
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                DiagLog.Write($"SetupMode EXIT 1: write failed at '{path}': {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"Failed to write bootstrap.json: {ex.Message}");
                return Task.FromResult(1);
            }
        }
    }

    /// <summary>
    /// Legacy single-shot template render — the M0A behavior, retained for the MSI
    /// Scheduled Task channel and for direct lab/dev launches. Argv-driven, no hub.
    /// </summary>
    internal static class DiagnosticMode
    {
        public static Task<int> RunAsync(string[] args)
        {
            var options = AgentOptions.Parse(args);

            if (!ToastTemplateCatalog.All.TryGetValue(options.Template, out var template))
            {
                Console.Error.WriteLine($"Unknown template: {options.Template}");
                DiagLog.Write($"DiagnosticMode EXIT 4: unknown template '{options.Template}'.");
                return Task.FromResult(4);
            }

            try
            {
                AppNotificationManager.Default.NotificationInvoked += (_, activationArgs) =>
                {
                    Console.WriteLine($"Notification activated: {activationArgs.Argument}");
                    DiagLog.Write($"DiagnosticMode NotificationInvoked: argument='{activationArgs.Argument}'");
                };

                AppNotificationManager.Default.Register();
                DiagLog.Write("DiagnosticMode: Register() returned.");

                var assets = new FileSystemToastAssets(AppContext.BaseDirectory);
                WarnIfAssetsMissing(template, assets);

                var notification = ToastTemplateBuilder.Build(template, assets, options.OverrideTitle, options.OverrideBody);
                // Legacy-WinRT toasts deliver clicks via ToastNotification.Activated, not the
                // NotificationInvoked subscription above (which only fires for WinAppSDK Show()).
                // Wire the in-process callback so `--template` is a server-free activation test.
                LegacyToastShim.Show(notification, arg =>
                {
                    Console.WriteLine($"Notification activated: {arg}");
                    DiagLog.Write($"DiagnosticMode activation: argument='{arg}'");
                });
                DiagLog.Write($"DiagnosticMode: Show() returned. Template={template.Key}");

                Console.WriteLine($"Toast Notification sent. Template: {template.Key}");

                if (options.WaitSeconds > 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(options.WaitSeconds));
                }

                AppNotificationManager.Default.Unregister();
                DiagLog.Write("DiagnosticMode EXIT 0: clean.");
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to send Toast Notification.");
                Console.Error.WriteLine(ex);
                DiagLog.Write($"DiagnosticMode EXIT 1: {ex.GetType().FullName}: {ex.Message}");
                return Task.FromResult(1);
            }
        }

        private static void WarnIfAssetsMissing(ToastTemplate template, IToastAssets assets)
        {
            if (template.UseHeroImage && assets.HeroImageUri is null)
                Console.Error.WriteLine("Warning: hero image asset missing; toast will render without hero.");
            if (template.UseAppLogoOverride && assets.AppLogoUri is null)
                Console.Error.WriteLine("Warning: app logo asset missing; toast will render without logo override.");
        }
    }

    /// <summary>
    /// Long-running primary worker. Loads config (or runs first-run registration),
    /// connects to the hub, renders incoming notifications until shutdown.
    /// </summary>
    internal static class PrimaryMode
    {
        public static async Task<int> RunAsync(string[] _)
        {
            // Registration retry loop: if the hub tells us this device was deleted,
            // config.json is cleared and we loop back to re-register immediately
            // without waiting for the next logon.
            while (true)
            {
                var config = ConfigStore.TryLoad();
                if (config is null)
                {
                    DiagLog.Write("PrimaryMode: no DeviceConfig — running first-run registration.");
                    config = await TryFirstRunRegistrationAsync();
                    if (config is null)
                    {
                        DiagLog.Write("PrimaryMode EXIT 9: agent not configured.");
                        return 9;
                    }
                }
#if !DEBUG
                else if (config.ServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    DiagLog.Write($"PrimaryMode EXIT 9: stored ServerUrl '{config.ServerUrl}' uses HTTP — HTTPS is required. Agent will not connect.");
                    return 9;
                }
#endif

                var reregister = false;
                try
                {
                    // Refresh tenant name + logo URL from the server so attribution
                    // (top of every Windows toast) stays accurate after a rename or
                    // logo re-upload, and for agents registered before either field
                    // existed in config.json. Best-effort: any error falls back to
                    // the cached name and the bundled icon.
                    string? localLogoPath = null;
                    using (var refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var info = await RegistrationService.TryRefreshTenantInfoAsync(config, refreshCts.Token);
                        if (!string.IsNullOrWhiteSpace(info.Name) && info.Name != config.TenantName)
                        {
                            config = config with { TenantName = info.Name };
                            ConfigStore.Save(config);
                            DiagLog.Write($"PrimaryMode: tenant name refreshed to '{info.Name}'.");
                        }

                        // Download tenant logo to local disk so it can be written
                        // as the AUMID IconUri below. Must complete BEFORE the first
                        // AppNotificationManager.Default touch — WinAppSDK snapshots
                        // the AUMID hive on first access. Passing the same CTS keeps
                        // the whole refresh bounded at 10s.
                        localLogoPath = await TenantLogoStore.DownloadAsync(info.LogoUrl, refreshCts.Token);
                    }

                    // Phase 1 — write tenant name + tenant-logo path under our declared
                    // AUMID before any AppNotificationManager.Default touch. Wins for
                    // legacy Shell32 AUMID consumers (jump lists, taskbar pin, anything
                    // that respects the process explicit AUMID).
                    NotificationDisplayName.Apply(config.TenantName, localLogoPath);

                    // Keep Register() after AgentHubClient construction. The constructor subscribes
                    // to NotificationInvoked, and Windows App SDK throws COMException 0x80070490
                    // if the event is subscribed after registration.
                    await using var client = new AgentHubClient(config);

                    // Wrap Register so SignalR still connects (and the device shows online in
                    // the dashboard) even on configurations where AppNotificationManager fails
                    // to initialize — e.g. elevated processes on locked-down Server SKUs, or
                    // sessions without an active Action Center. Visual toast delivery may be
                    // degraded; the device-online signal is more important to preserve.
                    var registerOk = false;
                    try
                    {
                        AppNotificationManager.Default.Register();
                        registerOk = true;
                        DiagLog.Write("PrimaryMode: Register() returned.");
                    }
                    catch (Exception ex)
                    {
                        DiagLog.Write($"PrimaryMode: AppNotificationManager.Register() failed — agent will run without local toast delivery. {ex.GetType().Name}: {ex.Message}");
                    }

                    // Phase 2 only runs when Register() succeeded — ApplyToActivatorAumids
                    // scans HKCU for the AUMID Register() just wrote. If Register failed,
                    // there's no AUMID to find and the scan is a no-op anyway.
                    if (registerOk)
                    {
                        // Register() just wrote a hash-derived AUMID for this unpackaged app
                        // and stamped our COM activator CLSID under it. Find that AUMID by
                        // scanning HKCU for entries whose CustomActivator matches our CLSID,
                        // and overwrite DisplayName/IconUri there so the toast attribution
                        // shows the tenant name + tenant logo. This is the AUMID Windows
                        // actually reads when rendering toasts — Phase 1's HKCU write is
                        // belt-and-suspenders for non-WinAppSDK consumers.
                        NotificationDisplayName.ApplyToActivatorAumids(config.TenantName, localLogoPath);
                    }

                    using var tray = new TrayIconService(config.ServerUrl);
                    client.ConnectionStateChanged += (_, state) => tray.UpdateState(state);

                    // M12 — desktop info overlay, hosted on the tray's STA thread
                    // (no second thread). Window is created lazily on first Apply,
                    // so a disabled overlay never realizes a window.
                    using var overlay = new DesktopOverlayService(tray.Post);

                    using var shutdown = new CancellationTokenSource();
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        try { shutdown.Cancel(); } catch (ObjectDisposedException) { }
                    };
                    tray.QuitRequested      += () => shutdown.Cancel();
                    tray.ReconnectRequested += async () =>
                    {
                        try { await client.ReconnectAsync(); }
                        catch (Exception ex) { DiagLog.Write($"ReconnectRequested: {ex.GetType().Name}: {ex.Message}"); }
                    };
                    tray.SendTestRequested += () =>
                    {
                        try
                        {
                            var assets = new FileSystemToastAssets(AppContext.BaseDirectory);
                            var tmpl   = ToastTemplateCatalog.All[ToastTemplateKey.Announcement];
                            var note   = ToastTemplateBuilder.Build(tmpl, assets,
                                "Toast Notification",
                                "Agent is connected. Notifications from your admin will appear here.");
                            LegacyToastShim.Show(note);
                            DiagLog.Write("PrimaryMode: test notification sent from tray.");
                        }
                        catch (Exception ex)
                        {
                            DiagLog.Write($"PrimaryMode: test notification failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    };

                    UpdateService.UpdateReady += version => tray.ShowUpdateAvailable(version);
                    tray.ApplyUpdateRequested += () =>
                    {
                        try { UpdateService.ApplyUpdateAndRestart(); }
                        catch (Exception ex) { DiagLog.Write($"PrimaryMode: ApplyUpdate failed: {ex.GetType().Name}: {ex.Message}"); }
                    };

                    var updateTask = Task.Run(() => UpdateService.RunUpdateLoopAsync(shutdown.Token));

                    client.OnDecommissioned += () =>
                    {
                        reregister = true;
                        shutdown.Cancel();
                    };

                    await client.StartAsync(shutdown.Token);
                    DiagLog.Write($"PrimaryMode: agent online (deviceId={config.DeviceId})");

                    // M12 — apply device appearance (desktop overlay + lock screen)
                    // AFTER going online so the device-online signal isn't delayed by
                    // image download/apply. Best-effort and non-fatal: any failure
                    // leaves whatever was last applied. MVP cadence is startup-only —
                    // admin changes take effect at next agent restart (live hub push
                    // is M12.B). Bounded at 30s so a slow image fetch can't hang here.
                    try
                    {
                        using var apCts = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                        apCts.CancelAfter(TimeSpan.FromSeconds(30));
                        var appearance = await RegistrationService.TryGetAppearanceConfigAsync(config, apCts.Token);
                        if (appearance is not null)
                        {
                            overlay.Apply(appearance.Overlay, config.TenantName);
                            await LockScreenService.ApplyAsync(appearance.LockScreen, apCts.Token);
                        }
                    }
                    catch (OperationCanceledException) { /* shutdown or 30s cap */ }
                    catch (Exception ex)
                    {
                        DiagLog.Write($"PrimaryMode: device appearance apply failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    try { await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token); }
                    catch (OperationCanceledException) { }
                }
                finally
                {
                    try { AppNotificationManager.Default.Unregister(); } catch { /* best-effort */ }
                }

                if (!reregister)
                {
                    DiagLog.Write("PrimaryMode EXIT 0: clean shutdown.");
                    return 0;
                }

                DiagLog.Write("PrimaryMode: device decommissioned — re-registering immediately.");
            }
        }

        private static async Task<DeviceConfig?> TryFirstRunRegistrationAsync()
        {
            var bootstrap = ConfigStore.TryLoadBootstrap();
            if (bootstrap is null)
            {
                DiagLog.Write("First-run registration: no bootstrap config (env vars or bootstrap.json).");
                return null;
            }

#if !DEBUG
            if (bootstrap.ServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                DiagLog.Write($"First-run registration: ServerUrl '{bootstrap.ServerUrl}' uses HTTP — HTTPS is required. Registration aborted.");
                return null;
            }
#endif

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var config = await RegistrationService.RegisterAsync(bootstrap, cts.Token);
            if (config is null) return null;

            ConfigStore.Save(config);
            DiagLog.Write($"First-run registration saved to '{ConfigStore.GetConfigPath()}'.");
            return config;
        }
    }

    /// <summary>
    /// Handles --diag: prints the log file path and the last 200 lines to stdout.
    /// Useful for remote support ("run --diag and paste the output").
    /// </summary>
    internal static class DiagMode
    {
        // OutputType=WinExe means the process is detached from any parent console at launch,
        // so Console.WriteLine goes nowhere when --diag is invoked from cmd/PowerShell. Reattach
        // to the parent console (if one exists) so support staff actually see the output.
        // ATTACH_PARENT_PROCESS = (uint)-1.
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint dwProcessId);

        public static int Run()
        {
            // Best-effort attach. Failure is fine (e.g. invoked with no parent console) —
            // we also dump the same content to a file so support always has something to read.
            try { AttachConsole(0xFFFFFFFFu); } catch { /* best-effort */ }

            var dumpPath = Path.Combine(Path.GetTempPath(), "toastnotification-diag.txt");
            using var sink = new DualWriter(dumpPath);

            sink.WriteLine($"Toast Notification Agent — version {ThisAssembly.Version}");
            sink.WriteLine($"OS: {Environment.OSVersion.VersionString}; 64-bit OS={Environment.Is64BitOperatingSystem}; user={Environment.UserDomainName}\\{Environment.UserName}; machine={Environment.MachineName}");
            sink.WriteLine();

            var path = DiagLog.LogFilePath;
            sink.WriteLine($"DiagLog path: {(string.IsNullOrEmpty(path) ? "(unavailable)" : path)}");

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                sink.WriteLine("Log file not found.");
                sink.WriteLine();
                sink.WriteLine($"Diag output also written to: {dumpPath}");
                return 0;
            }

            try
            {
                var info = new FileInfo(path);
                sink.WriteLine($"Log size: {info.Length / 1024.0:F1} KB");
                sink.WriteLine();
                sink.WriteLine("--- Last 200 lines ---");

                var lines = File.ReadAllLines(path);
                var start = Math.Max(0, lines.Length - 200);
                for (var i = start; i < lines.Length; i++)
                    sink.WriteLine(lines[i]);
            }
            catch (Exception ex)
            {
                sink.WriteLine($"Failed to read log: {ex.Message}");
            }

            sink.WriteLine();
            sink.WriteLine($"Diag output also written to: {dumpPath}");
            return 0;
        }

        // Writes to both stdout (when AttachConsole succeeded) and a temp-file copy.
        // Either side may be unavailable; both are best-effort.
        private sealed class DualWriter : IDisposable
        {
            private readonly StreamWriter? _file;

            public DualWriter(string filePath)
            {
                try { _file = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8); }
                catch { _file = null; }
            }

            public void WriteLine(string line = "")
            {
                try { Console.WriteLine(line); } catch { /* no console attached */ }
                try { _file?.WriteLine(line); } catch { /* file write failed */ }
            }

            public void Dispose()
            {
                try { _file?.Flush(); _file?.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    internal static class DiagLog
    {
        private const long MaxFileBytes = 512 * 1024; // 512 KB — rotate at this threshold

        private static readonly object _lock = new();
        public static string LogFilePath { get; private set; } = "";
        public static bool IsPackaged { get; private set; }

        public static void Init()
        {
            string dir;
            try
            {
                dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                IsPackaged = true;
            }
            catch
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Toast2IT", "Toast Notification");
                IsPackaged = false;
            }
            try
            {
                Directory.CreateDirectory(dir);
                LogFilePath = Path.Combine(dir, "agent.log");
            }
            catch
            {
                LogFilePath = "";
            }
        }

        public static void Write(string message)
        {
            if (string.IsNullOrEmpty(LogFilePath)) return;
            var line = $"{DateTime.UtcNow:O} {message}{Environment.NewLine}";
            try
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch { /* DiagLog must never throw */ }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogFilePath)) return;
                if (new FileInfo(LogFilePath).Length < MaxFileBytes) return;

                var rolled = LogFilePath + ".1";
                if (File.Exists(rolled)) File.Delete(rolled);
                File.Move(LogFilePath, rolled);
            }
            catch { /* best-effort rotation */ }
        }
    }

    internal sealed record AgentOptions(
        ToastTemplateKey Template,
        string? OverrideTitle,
        string? OverrideBody,
        int WaitSeconds)
    {
        public static AgentOptions Parse(string[] args)
        {
            var template      = ToastTemplateKey.Plain;
            string? title     = null;
            string? body      = null;
            var waitSeconds   = 10;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--template" when i + 1 < args.Length:
                        if (ToastTemplateCatalog.TryParseKey(args[++i], out var parsed))
                        {
                            template = parsed;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Warning: unknown --template '{args[i]}', falling back to plain.");
                        }
                        break;
                    case "--title" when i + 1 < args.Length:
                        title = args[++i];
                        break;
                    case "--body" when i + 1 < args.Length:
                        body = args[++i];
                        break;
                    case "--wait" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedWaitSeconds):
                        waitSeconds = Math.Max(0, parsedWaitSeconds);
                        i++;
                        break;
                    case "--no-wait":
                        waitSeconds = 0;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsageAndExit();
                        break;
                }
            }

            return new AgentOptions(template, title, body, waitSeconds);
        }

        private static void PrintUsageAndExit()
        {
            Console.WriteLine("Usage: ToastNotification.Agent [options]");
            Console.WriteLine();
            Console.WriteLine("  (no args)                           connect to the backend hub and run as primary worker");
            Console.WriteLine("  --template <name>                   diagnostic single-shot render: plain | announcement | alert | action | reminder | celebration | maintenance");
            Console.WriteLine("  --title <text>                      override the template title");
            Console.WriteLine("  --body <text>                       override the first body line");
            Console.WriteLine("  --wait <seconds>                    seconds to wait for activation (default 10)");
            Console.WriteLine("  --no-wait                           do not wait after sending");
            Console.WriteLine("  --setup-bootstrap <tenantId> <url>  MSI installer hook: write bootstrap.json (runs as SYSTEM)");
            Environment.Exit(0);
        }
    }
}
