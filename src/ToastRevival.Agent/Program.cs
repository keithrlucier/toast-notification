using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Security.Principal;

var options = AgentOptions.Parse(args);

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
{
    Console.Error.WriteLine("ToastRevival.Agent requires Windows 10 2004 / build 19041 or later for this spike.");
    return 2;
}

if (IsElevated())
{
    Console.Error.WriteLine("App notifications are not supported for elevated/admin processes. Run this spike unelevated.");
    return 3;
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
        var notification = new AppNotificationBuilder()
            .AddArgument("source", "m0a")
            .AddText(options.Title)
            .AddText(options.Body)
            .AddButton(new AppNotificationButton("Acknowledge")
                .AddArgument("source", "m0a")
                .AddArgument("action", "acknowledge"))
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);

        Console.WriteLine("ToastRevival M0A notification sent.");
        Console.WriteLine($"Title: {options.Title}");
        Console.WriteLine($"Body:  {options.Body}");

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
    Console.Error.WriteLine("Failed to send ToastRevival M0A notification.");
    Console.Error.WriteLine(ex);
    return 1;
}

static bool IsElevated()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

internal sealed record AgentOptions(string Title, string Body, int WaitSeconds)
{
    public static AgentOptions Parse(string[] args)
    {
        var title = "ToastRevival agent spike";
        var body = "M0A local Windows App SDK notification is working.";
        var waitSeconds = 10;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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

        return new AgentOptions(title, body, waitSeconds);
    }

    private static void PrintUsageAndExit()
    {
        Console.WriteLine("Usage: ToastRevival.Agent [--title <text>] [--body <text>] [--wait <seconds>|--no-wait]");
        Environment.Exit(0);
    }
}
