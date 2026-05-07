using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ToastRevival.Agent;

internal enum ToastTemplateKey
{
    Plain,
    Announcement,
    Alert,
    ActionRequired,
    Reminder,
    Celebration,
    Maintenance,
}

internal sealed record ToastTemplate(
    ToastTemplateKey Key,
    string Title,
    string BodyLine1,
    string? BodyLine2,
    bool UseHeroImage,
    bool UseAppLogoOverride,
    IReadOnlyList<ToastTemplateButton> Buttons,
    AppNotificationScenario Scenario,
    AppNotificationSoundEvent? Sound);

internal sealed record ToastTemplateButton(string Label, string Action, bool IsPrimary = false);

internal static class ToastTemplateCatalog
{
    public static ToastTemplate Plain { get; } = new(
        ToastTemplateKey.Plain,
        Title: "ToastRevival agent spike",
        BodyLine1: "M0A local Windows App SDK notification is working.",
        BodyLine2: null,
        UseHeroImage: false,
        UseAppLogoOverride: false,
        Buttons: new[] { new ToastTemplateButton("Acknowledge", "acknowledge", IsPrimary: true) },
        Scenario: AppNotificationScenario.Default,
        Sound: null);

    public static ToastTemplate Announcement { get; } = new(
        ToastTemplateKey.Announcement,
        Title: "Company announcement",
        BodyLine1: "New security policy takes effect Monday.",
        BodyLine2: "Click below for the full policy update.",
        UseHeroImage: true,
        UseAppLogoOverride: true,
        Buttons: new[] { new ToastTemplateButton("View details", "view_details", IsPrimary: true) },
        Scenario: AppNotificationScenario.Default,
        Sound: AppNotificationSoundEvent.Default);

    public static ToastTemplate Alert { get; } = new(
        ToastTemplateKey.Alert,
        Title: "Security alert",
        BodyLine1: "Suspicious sign-in attempt detected on your account.",
        BodyLine2: "Acknowledge if this was you, or report to IT.",
        UseHeroImage: true,
        UseAppLogoOverride: true,
        Buttons: new[]
        {
            new ToastTemplateButton("Acknowledge", "acknowledge", IsPrimary: true),
            new ToastTemplateButton("Report to IT",  "report_it"),
        },
        Scenario: AppNotificationScenario.Urgent,
        Sound: AppNotificationSoundEvent.Alarm);

    public static ToastTemplate ActionRequired { get; } = new(
        ToastTemplateKey.ActionRequired,
        Title: "Action required",
        BodyLine1: "Your password expires in 24 hours.",
        BodyLine2: null,
        UseHeroImage: false,
        UseAppLogoOverride: true,
        Buttons: new[]
        {
            new ToastTemplateButton("Reset now",     "reset_password", IsPrimary: true),
            new ToastTemplateButton("Remind later",  "remind_later"),
        },
        Scenario: AppNotificationScenario.Default,
        Sound: AppNotificationSoundEvent.Reminder);

    public static ToastTemplate Reminder { get; } = new(
        ToastTemplateKey.Reminder,
        Title: "Maintenance reminder",
        BodyLine1: "Scheduled maintenance starts in 30 minutes.",
        BodyLine2: "Save your work. Estimated downtime: 15 minutes.",
        UseHeroImage: false,
        UseAppLogoOverride: true,
        Buttons: new[] { new ToastTemplateButton("Got it", "ack_reminder", IsPrimary: true) },
        Scenario: AppNotificationScenario.Reminder,
        Sound: AppNotificationSoundEvent.Reminder);

    public static ToastTemplate Celebration { get; } = new(
        ToastTemplateKey.Celebration,
        Title: "Welcome to the team!",
        BodyLine1: "Glad to have you on board.",
        BodyLine2: null,
        UseHeroImage: true,
        UseAppLogoOverride: true,
        Buttons: new[] { new ToastTemplateButton("Thanks", "thanks", IsPrimary: true) },
        Scenario: AppNotificationScenario.Default,
        Sound: AppNotificationSoundEvent.Default);

    public static ToastTemplate Maintenance { get; } = new(
        ToastTemplateKey.Maintenance,
        Title: "Scheduled maintenance window",
        BodyLine1: "Email server reboot tonight 9:00 PM ET.",
        BodyLine2: "Expected downtime: 5 minutes. No action needed.",
        UseHeroImage: false,
        UseAppLogoOverride: true,
        Buttons: new[]
        {
            new ToastTemplateButton("Details",      "view_details", IsPrimary: true),
            new ToastTemplateButton("Acknowledge",  "acknowledge"),
        },
        Scenario: AppNotificationScenario.Default,
        Sound: AppNotificationSoundEvent.Default);

    public static IReadOnlyDictionary<ToastTemplateKey, ToastTemplate> All { get; } =
        new Dictionary<ToastTemplateKey, ToastTemplate>
        {
            [ToastTemplateKey.Plain]          = Plain,
            [ToastTemplateKey.Announcement]   = Announcement,
            [ToastTemplateKey.Alert]          = Alert,
            [ToastTemplateKey.ActionRequired] = ActionRequired,
            [ToastTemplateKey.Reminder]       = Reminder,
            [ToastTemplateKey.Celebration]    = Celebration,
            [ToastTemplateKey.Maintenance]    = Maintenance,
        };

    public static bool TryParseKey(string value, out ToastTemplateKey key)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "plain":          key = ToastTemplateKey.Plain;          return true;
            case "announcement":   key = ToastTemplateKey.Announcement;   return true;
            case "alert":          key = ToastTemplateKey.Alert;          return true;
            case "action":
            case "action-required":
            case "actionrequired": key = ToastTemplateKey.ActionRequired; return true;
            case "reminder":       key = ToastTemplateKey.Reminder;       return true;
            case "celebration":    key = ToastTemplateKey.Celebration;    return true;
            case "maintenance":    key = ToastTemplateKey.Maintenance;    return true;
            default:               key = ToastTemplateKey.Plain;          return false;
        }
    }
}

internal static class ToastTemplateBuilder
{
    public static AppNotification Build(ToastTemplate template, IToastAssets assets, string? overrideTitle, string? overrideBody)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument("source", "m0a")
            .AddArgument("template", template.Key.ToString());

        builder.AddText(overrideTitle ?? template.Title);
        builder.AddText(overrideBody  ?? template.BodyLine1);
        if (template.BodyLine2 is { Length: > 0 } body2)
        {
            builder.AddText(body2);
        }

        if (template.UseAppLogoOverride && assets.AppLogoUri is { } logoUri)
        {
            builder.SetAppLogoOverride(logoUri, AppNotificationImageCrop.Default);
        }

        if (template.UseHeroImage && assets.HeroImageUri is { } heroUri)
        {
            builder.SetHeroImage(heroUri);
        }

        if (template.Scenario != AppNotificationScenario.Default)
        {
            builder.SetScenario(template.Scenario);
        }

        if (template.Sound.HasValue)
        {
            builder.SetAudioEvent(template.Sound.Value);
        }

        foreach (var b in template.Buttons)
        {
            var button = new AppNotificationButton(b.Label)
                .AddArgument("source", "m0a")
                .AddArgument("template", template.Key.ToString())
                .AddArgument("action", b.Action);

            if (b.IsPrimary)
            {
                button.SetButtonStyle(AppNotificationButtonStyle.Success);
            }

            builder.AddButton(button);
        }

        return builder.BuildNotification();
    }
}

internal interface IToastAssets
{
    Uri? HeroImageUri    { get; }
    Uri? AppLogoUri      { get; }
    Uri? InlineImageUri  { get; }
}

internal sealed class FileSystemToastAssets : IToastAssets
{
    public FileSystemToastAssets(string baseDirectory)
    {
        HeroImageUri    = TryFileUri(Path.Combine(baseDirectory, "Assets", "toast-hero.png"));
        AppLogoUri      = TryFileUri(Path.Combine(baseDirectory, "Assets", "toast-logo.png"));
        InlineImageUri  = TryFileUri(Path.Combine(baseDirectory, "Assets", "toast-inline.png"));
    }

    public Uri? HeroImageUri    { get; }
    public Uri? AppLogoUri      { get; }
    public Uri? InlineImageUri  { get; }

    private static Uri? TryFileUri(string absolutePath)
        => File.Exists(absolutePath) ? new Uri(absolutePath, UriKind.Absolute) : null;
}
