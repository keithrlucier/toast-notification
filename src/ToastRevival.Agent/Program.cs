using System.Security.Principal;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using ToastRevival.Agent;

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
    ///  2. Diagnostic mode:   args contain --template — legacy M0A behavior. Used by the MSI
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

            // Setup mode: invoked by the MSI installer (as SYSTEM) to write bootstrap.json.
            // MUST be checked before the elevation guard — SYSTEM is elevated.
            if (HasFlag(args, "--setup-bootstrap"))
            {
                return await SetupMode.RunAsync(args);
            }

            if (IsElevated())
            {
                Console.Error.WriteLine("App notifications are not supported for elevated/admin processes. Run unelevated.");
                DiagLog.Write("EXIT 3: process is elevated.");
                return 3;
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

                using var postCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var ok = await InteractionFallback.PostAsync(config, notificationId, action, postCts.Token);

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

            // Expect: --setup-bootstrap <tenantId> <serverUrl>
            var idx = Array.IndexOf(args, "--setup-bootstrap");
            if (idx < 0 || idx + 2 >= args.Length)
            {
                DiagLog.Write("SetupMode EXIT 1: usage: --setup-bootstrap <tenantId> <serverUrl>");
                Console.Error.WriteLine("Usage: ToastNotification.Agent --setup-bootstrap <tenantId> <serverUrl>");
                return Task.FromResult(1);
            }

            var tenantIdStr = args[idx + 1];
            var serverUrl   = args[idx + 2];

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

            var bootstrap = new BootstrapConfig(tenantId, serverUrl);
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
                AppNotificationManager.Default.Show(notification);
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
            var config = ConfigStore.TryLoad();
            if (config is null)
            {
                DiagLog.Write("PrimaryMode: no DeviceConfig — running first-run registration.");
                config = await TryFirstRunRegistrationAsync();
                if (config is null)
                {
                    Console.Error.WriteLine("Toast Notification agent is not configured. Set TOAST_TENANT_ID and TOAST_SERVER_URL, or have the installer drop bootstrap.json next to the exe.");
                    DiagLog.Write("PrimaryMode EXIT 9: agent not configured.");
                    return 9;
                }
            }

            // Register the activator before the hub starts — same Register()/Unregister()
            // lifecycle that has been working since FIX-MSIX-004 in M0 D2.
            AppNotificationManager.Default.Register();
            DiagLog.Write("PrimaryMode: Register() returned.");

            try
            {
                await using var client = new AgentHubClient(config);

                // Tray icon is visible from process start (Connecting state) so the
                // user sees the agent in their tray at logon before the hub connects.
                using var tray = new TrayIconService(config.ServerUrl);
                client.ConnectionStateChanged += (_, state) => tray.UpdateState(state);

                // Ctrl+C / SIGTERM and tray Quit both cancel shutdown.
                using var shutdown = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    shutdown.Cancel();
                };
                tray.QuitRequested    += () => shutdown.Cancel();
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
                        var note   = ToastTemplateBuilder.Build(tmpl, assets, "Test Notification", "Toast Notification agent is connected and working.");
                        AppNotificationManager.Default.Show(note);
                        DiagLog.Write("PrimaryMode: test notification sent from tray.");
                    }
                    catch (Exception ex)
                    {
                        DiagLog.Write($"PrimaryMode: test notification failed: {ex.GetType().Name}: {ex.Message}");
                    }
                };

                await client.StartAsync(shutdown.Token);
                Console.WriteLine($"Toast Notification agent online (deviceId={config.DeviceId}). Ctrl+C to exit.");

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
                }
                catch (OperationCanceledException) { /* shutdown */ }

                DiagLog.Write("PrimaryMode EXIT 0: clean shutdown.");
                return 0;
            }
            finally
            {
                try { AppNotificationManager.Default.Unregister(); } catch { /* best-effort */ }
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var config = await RegistrationService.RegisterAsync(bootstrap, cts.Token);
            if (config is null) return null;

            ConfigStore.Save(config);
            DiagLog.Write($"First-run registration saved to '{ConfigStore.GetConfigPath()}'.");
            return config;
        }
    }

    internal static class DiagLog
    {
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
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch { /* DiagLog must never throw */ }
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
