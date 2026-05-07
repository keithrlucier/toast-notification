using Microsoft.Windows.AppNotifications;
using System.Security.Principal;
using ToastRevival.Agent;

var options = AgentOptions.Parse(args);

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
{
    Console.Error.WriteLine("Toast Notification agent requires Windows 10 2004 / build 19041 or later for this spike.");
    return 2;
}

if (IsElevated())
{
    Console.Error.WriteLine("App notifications are not supported for elevated/admin processes. Run this spike unelevated.");
    return 3;
}

if (!ToastTemplateCatalog.All.TryGetValue(options.Template, out var template))
{
    Console.Error.WriteLine($"Unknown template: {options.Template}");
    return 4;
}

try
{
    var registered = false;

    AppNotificationManager.Default.NotificationInvoked += (_, activationArgs) =>
    {
        Console.WriteLine($"Notification activated: {activationArgs.Argument}");
    };

    AppNotificationManager.Default.Register();
    registered = true;

    try
    {
        var assets = new FileSystemToastAssets(AppContext.BaseDirectory);
        WarnIfAssetsMissing(template, assets);

        var notification = ToastTemplateBuilder.Build(template, assets, options.OverrideTitle, options.OverrideBody);
        AppNotificationManager.Default.Show(notification);

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

        return 0;
    }
    finally
    {
        if (registered)
        {
            AppNotificationManager.Default.Unregister();
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to send Toast Notification.");
    Console.Error.WriteLine(ex);
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
