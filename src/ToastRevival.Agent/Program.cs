using Microsoft.Windows.AppNotifications;
using System.Security.Principal;
using ToastRevival.Agent;

DiagLog.Init();
DiagLog.Write($"==> Toast Notification agent start; pid={Environment.ProcessId}; args=[{string.Join(' ', args)}]; baseDir={AppContext.BaseDirectory}; packaged={DiagLog.IsPackaged}; logPath={DiagLog.LogFilePath}");

var options = AgentOptions.Parse(args);

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
{
    Console.Error.WriteLine("Toast Notification agent requires Windows 10 2004 / build 19041 or later.");
    DiagLog.Write("EXIT 2: runtime gate IsWindowsVersionAtLeast(10,0,19041) failed.");
    return 2;
}

if (IsElevated())
{
    Console.Error.WriteLine("App notifications are not supported for elevated/admin processes. Run this spike unelevated.");
    DiagLog.Write("EXIT 3: process is elevated. App notifications require non-elevated context.");
    return 3;
}

if (!ToastTemplateCatalog.All.TryGetValue(options.Template, out var template))
{
    Console.Error.WriteLine($"Unknown template: {options.Template}");
    DiagLog.Write($"EXIT 4: unknown template '{options.Template}'.");
    return 4;
}

try
{
    var registered = false;

    AppNotificationManager.Default.NotificationInvoked += (_, activationArgs) =>
    {
        Console.WriteLine($"Notification activated: {activationArgs.Argument}");
        DiagLog.Write($"NotificationInvoked: argument='{activationArgs.Argument}'");
    };

    DiagLog.Write("Calling AppNotificationManager.Default.Register()...");
    AppNotificationManager.Default.Register();
    registered = true;
    DiagLog.Write("Register() returned without throwing.");

    try
    {
        var assets = new FileSystemToastAssets(AppContext.BaseDirectory);
        WarnIfAssetsMissing(template, assets);
        DiagLog.Write($"Assets resolved: hero={assets.HeroImageUri?.ToString() ?? "(missing)"}; logo={assets.AppLogoUri?.ToString() ?? "(missing)"}; inline={assets.InlineImageUri?.ToString() ?? "(missing)"}");

        var notification = ToastTemplateBuilder.Build(template, assets, options.OverrideTitle, options.OverrideBody);
        DiagLog.Write($"Notification built. Template={template.Key}; Scenario={template.Scenario}; Sound={(template.Sound?.ToString() ?? "(none)")}; Buttons={template.Buttons.Count}");
        DiagLog.Write("Calling AppNotificationManager.Default.Show()...");
        AppNotificationManager.Default.Show(notification);
        DiagLog.Write($"Show() returned without throwing. Notification.Id={notification.Id}; ExpiresOnReboot={notification.ExpiresOnReboot}");

        Console.WriteLine($"Toast Notification sent. Template: {template.Key}");
        Console.WriteLine($"Title: {options.OverrideTitle ?? template.Title}");
        Console.WriteLine($"Body:  {options.OverrideBody  ?? template.BodyLine1}");
        if (template.BodyLine2 is { Length: > 0 } body2)
        {
            Console.WriteLine($"Body2: {body2}");
        }
        Console.WriteLine($"Scenario: {template.Scenario}, Sound: {(template.Sound?.ToString() ?? "(none)")}, Buttons: {template.Buttons.Count}");

        if (options.WaitSeconds > 0)
        {
            Console.WriteLine($"Waiting {options.WaitSeconds} seconds for activation. Press Ctrl+C to exit early.");
            await Task.Delay(TimeSpan.FromSeconds(options.WaitSeconds));
        }

        DiagLog.Write("EXIT 0: clean.");
        return 0;
    }
    finally
    {
        if (registered)
        {
            DiagLog.Write("Calling AppNotificationManager.Default.Unregister()...");
            AppNotificationManager.Default.Unregister();
            DiagLog.Write("Unregister() returned.");
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to send Toast Notification.");
    Console.Error.WriteLine(ex);
    DiagLog.Write($"EXIT 1: exception {ex.GetType().FullName}: {ex.Message}\n{ex}");
    return 1;
}

static bool IsElevated()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

static void WarnIfAssetsMissing(ToastTemplate template, IToastAssets assets)
{
    if (template.UseHeroImage && assets.HeroImageUri is null)
    {
        Console.Error.WriteLine("Warning: hero image asset missing; toast will render without hero.");
    }
    if (template.UseAppLogoOverride && assets.AppLogoUri is null)
    {
        Console.Error.WriteLine("Warning: app logo asset missing; toast will render without logo override.");
    }
}

namespace ToastRevival.Agent
{
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
            catch
            {
            }
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
            Console.WriteLine("  --template <name>   plain | announcement | alert | action | reminder | celebration | maintenance");
            Console.WriteLine("  --title <text>      override the template title");
            Console.WriteLine("  --body <text>       override the first body line");
            Console.WriteLine("  --wait <seconds>    seconds to wait for activation (default 10)");
            Console.WriteLine("  --no-wait           do not wait after sending");
            Environment.Exit(0);
        }
    }
}
