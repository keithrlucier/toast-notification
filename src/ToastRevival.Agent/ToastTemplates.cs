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
    /// <summary>
    /// A minimal plain-text notification with no images and no sound.
    /// Use for low-priority informational messages that require no immediate action.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system's default notification timer
    /// (typically 5–7 seconds on Windows 11). Moves to Action Center on dismiss.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Default"/> — no special behavior;
    /// respects Do Not Disturb / Focus Assist.
    /// </para>
    /// <para><b>Images:</b> None.</para>
    /// <para><b>Sound:</b> Silent.</para>
    /// <para><b>Buttons:</b> Acknowledge.</para>
    /// </summary>
    public static ToastTemplate Plain { get; } = new(
        ToastTemplateKey.Plain,
        Title: "Toast Notification agent spike",
        BodyLine1: "M0A local Windows App SDK notification is working.",
        BodyLine2: null,
        UseHeroImage: false,
        UseAppLogoOverride: false,
        Buttons: new[] { new ToastTemplateButton("Acknowledge", "acknowledge", IsPrimary: true) },
        Scenario: AppNotificationScenario.Default,
        Sound: null);

    /// <summary>
    /// A branded announcement with a full-width hero image and tenant logo.
    /// Use for company-wide communications — policy updates, news, scheduled events.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system's default notification timer.
    /// Moves to Action Center on dismiss.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Default"/> — no special behavior;
    /// respects Do Not Disturb / Focus Assist.
    /// </para>
    /// <para><b>Images:</b> Hero image (364×180) + tenant logo override.</para>
    /// <para><b>Sound:</b> System default notification sound.</para>
    /// <para><b>Buttons:</b> View details.</para>
    /// </summary>
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

    /// <summary>
    /// A high-priority security alert that breaks through Do Not Disturb and Focus Assist sessions.
    /// Use for time-sensitive security events that must reach the user regardless of their focus state —
    /// sign-in anomalies, account lockouts, threat detections.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system timer. Does NOT stay on screen indefinitely —
    /// use <see cref="Reminder"/> if you need persistent-until-dismissed behavior. The looping
    /// <see cref="AppNotificationSoundEvent.Alarm"/> audio continues until the user acts or dismisses.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Urgent"/> (Windows 11 22H2+) — bypasses
    /// Focus Assist and Do Not Disturb. On older Windows versions this degrades to Default behavior.
    /// </para>
    /// <para><b>Images:</b> Hero image (364×180) + tenant logo override.</para>
    /// <para><b>Sound:</b> Looping alarm audio (<see cref="AppNotificationSoundEvent.Alarm"/>).</para>
    /// <para><b>Buttons:</b> Acknowledge (primary), Report to IT.</para>
    /// </summary>
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

    /// <summary>
    /// An actionable prompt for tasks the user must complete — password expiry, compliance deadlines,
    /// certificate renewals. Offers a direct action button alongside a deferral option.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system's default notification timer. Despite the
    /// reminder sound, this template uses <see cref="AppNotificationScenario.Default"/> and does NOT
    /// stay on screen indefinitely. Use <see cref="Reminder"/> if the notification must persist until
    /// the user explicitly dismisses it.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Default"/> — respects Do Not Disturb /
    /// Focus Assist. The <see cref="AppNotificationSoundEvent.Reminder"/> is a sound choice only;
    /// it does not change persistence behavior.
    /// </para>
    /// <para><b>Images:</b> Tenant logo override only (no hero image).</para>
    /// <para><b>Sound:</b> Reminder chime (<see cref="AppNotificationSoundEvent.Reminder"/>).</para>
    /// <para><b>Buttons:</b> Reset now (primary), Remind later.</para>
    /// </summary>
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

    /// <summary>
    /// The only built-in template that persists on screen until the user explicitly dismisses it.
    /// Use for maintenance windows, outage notices, or any message that must not silently expire —
    /// situations where a user who misses the notification could be caught off guard by a system event.
    /// <para>
    /// <b>Persistence:</b> <b>Stays on screen indefinitely until dismissed.</b> The banner does not
    /// auto-dismiss on the system timer. The notification remains in Action Center after dismissal.
    /// This is the correct template when delivery confirmation matters.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Reminder"/> — Windows keeps the banner
    /// visible until the user acts on it or explicitly closes it. Respects Do Not Disturb /
    /// Focus Assist (will not break through focus sessions — use <see cref="Alert"/> if that is
    /// required).
    /// </para>
    /// <para><b>Images:</b> Tenant logo override only (no hero image).</para>
    /// <para><b>Sound:</b> Reminder chime (<see cref="AppNotificationSoundEvent.Reminder"/>).</para>
    /// <para><b>Buttons:</b> Got it (primary).</para>
    /// </summary>
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

    /// <summary>
    /// A visually rich, branded notification for positive milestones — new hire onboarding,
    /// certifications, anniversaries, team recognition. The hero image makes it stand out
    /// against standard system notifications.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system's default notification timer.
    /// Moves to Action Center on dismiss.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Default"/> — no special behavior;
    /// respects Do Not Disturb / Focus Assist.
    /// </para>
    /// <para><b>Images:</b> Hero image (364×180) + tenant logo override.</para>
    /// <para><b>Sound:</b> System default notification sound.</para>
    /// <para><b>Buttons:</b> Thanks (primary).</para>
    /// </summary>
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

    /// <summary>
    /// A compact, text-only notification for upcoming maintenance windows, patch cycles,
    /// or service interruptions. No images keep it lightweight and fast to render on
    /// endpoints with limited bandwidth or constrained GPU resources.
    /// <para>
    /// <b>Persistence:</b> Auto-dismisses per the system's default notification timer.
    /// Moves to Action Center on dismiss. If the notification must stay on screen until
    /// acknowledged, use <see cref="Reminder"/> instead.
    /// </para>
    /// <para>
    /// <b>Scenario:</b> <see cref="AppNotificationScenario.Default"/> — no special behavior;
    /// respects Do Not Disturb / Focus Assist.
    /// </para>
    /// <para><b>Images:</b> Tenant logo override only (no hero image).</para>
    /// <para><b>Sound:</b> System default notification sound.</para>
    /// <para><b>Buttons:</b> Details (primary), Acknowledge.</para>
    /// </summary>
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

    /// <summary>
    /// Build an AppNotification from a server-pushed payload (M2 SignalR path).
    /// Shares no template assumptions with the argv-driven Build above; both paths
    /// drive the same AppNotificationBuilder API. The notificationId is encoded in
    /// every argument set so NotificationInvoked can route the click back to the
    /// correct delivery row.
    /// </summary>
    public static AppNotification BuildFromPayload(ToastPayload p)
    {
        var notificationId = p.NotificationId.ToString();

        var builder = new AppNotificationBuilder()
            .AddArgument("source", "hub")
            .AddArgument("notificationId", notificationId);

        builder.AddText(p.Title);
        if (p.BodyLine1 is { Length: > 0 } b1) builder.AddText(b1);
        if (p.BodyLine2 is { Length: > 0 } b2) builder.AddText(b2);

        if (TryUri(p.LogoUrl, out var logoUri))
        {
            builder.SetAppLogoOverride(logoUri, AppNotificationImageCrop.Default);
        }

        if (TryUri(p.HeroImageUrl, out var heroUri))
        {
            builder.SetHeroImage(heroUri);
        }

        if (TryParseScenario(p.Scenario, out var scenario) && scenario != AppNotificationScenario.Default)
        {
            builder.SetScenario(scenario);
        }

        ApplyAudio(builder, p.AudioSetting);

        if (p.ActionButtons is not null)
        {
            var index = 0;
            foreach (var b in p.ActionButtons)
            {
                index++;
                if (string.IsNullOrWhiteSpace(b.Label)) continue;

                var action = ButtonAction(b, index);
                var button = new AppNotificationButton(b.Label)
                    .AddArgument("source", "hub")
                    .AddArgument("notificationId", notificationId)
                    .AddArgument("action", action);

                if (TryHttpUri(b.Url, out var buttonUri))
                {
                    button.AddArgument("url", buttonUri.AbsoluteUri);
                }

                ApplyButtonStyle(button, b);
                builder.AddButton(button);
            }
        }

        return builder.BuildNotification();
    }

    // Server-supplied image URLs (logo/hero) are handed to the Windows image
    // loader, which will dereference file://, UNC (\\host\share parses as file://),
    // and other local schemes — leaking NetNTLM creds / enabling SSRF. Restrict to
    // http(s) only, mirroring TryHttpUri, so only remote web images are fetched.
    private static bool TryUri(string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static bool TryHttpUri(string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string ButtonAction(PayloadButton button, int index)
    {
        if (!string.IsNullOrWhiteSpace(button.Action)) return button.Action.Trim();
        if (!string.IsNullOrWhiteSpace(button.ActionId)) return button.ActionId.Trim();
        return $"button_{index}";
    }

    private static void ApplyButtonStyle(AppNotificationButton button, PayloadButton source)
    {
        if (string.Equals(source.Style, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            button.SetButtonStyle(AppNotificationButtonStyle.Critical);
            return;
        }

        if (source.IsPrimary || string.Equals(source.Style, "Success", StringComparison.OrdinalIgnoreCase))
        {
            button.SetButtonStyle(AppNotificationButtonStyle.Success);
        }
    }

    private static bool TryParseScenario(string? value, out AppNotificationScenario scenario)
    {
        scenario = AppNotificationScenario.Default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value, ignoreCase: true, out scenario);
    }

    private static void ApplyAudio(AppNotificationBuilder builder, string? audioSetting)
    {
        if (string.IsNullOrWhiteSpace(audioSetting)) return;

        // Treat `ms-winsoundevent:Notification.X` and `http(s)://...` URIs as audio URIs.
        // Anything else, try to parse as a known AppNotificationSoundEvent enum value.
        //
        // A server-supplied audio URI is dereferenced by the WinRT toast audio
        // pipeline, so restrict to safe schemes only:
        //   - ms-winsoundevent : the OS sound-event form (e.g. Notification.Default) —
        //                        the normal way to pick a system notification sound.
        //   - http / https     : remotely hosted custom audio.
        // Reject file:// (UNC / local-disk reads → NetNTLM leak / local file probe)
        // and ms-appx:// (package-local resource the server must not be able to target).
        if (Uri.TryCreate(audioSetting, UriKind.Absolute, out var uri)
            && (uri.Scheme is "http" or "https" or "ms-winsoundevent"))
        {
            builder.SetAudioUri(uri);
            return;
        }

        if (Enum.TryParse<AppNotificationSoundEvent>(audioSetting, ignoreCase: true, out var evt))
        {
            builder.SetAudioEvent(evt);
        }
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
