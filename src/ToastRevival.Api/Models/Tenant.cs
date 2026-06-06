namespace ToastRevival.Api.Models;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public string Subdomain { get; set; } = null!;

    // Per-tenant HMAC-SHA256 signing key (base64, 32 random bytes). Generated at tenant
    // creation, returned to agents at device registration so they can verify notification
    // payloads (and, as of AGT-4-R, the appearance/lock-screen config) before applying.
    // SECURITY INVARIANT: this key is NEVER exposed to admins, NEVER shown in the dashboard,
    // and has NO reset/rotate endpoint. It reaches a device only at registration and is held
    // there DPAPI-encrypted (config.json). A stolen device config grants only the ability to
    // VERIFY this tenant's payloads — not to forge new ones; forging requires the server DB.
    // REVIEW-2026-06-06 MT-M6 REJECTED-by-design: per-tenant HMAC signing key has no rotation endpoint; key rotation requires coordinated re-registration of all tenant devices; accepted architectural risk documented, planned as XT-4 milestone
    public string SigningKey { get; set; } = null!;

    // Optional pre-shared key required for device registration. When null,
    // device registration is open — any caller that knows the TenantId may
    // register. When set, the agent must include the matching key in its
    // RegisterDeviceRequest or registration is rejected.
    public string? EnrollmentKey { get; set; }

    // DC-H2: LicenseCount dead column removed — use BillingPlanRules/Stripe for limits.
    public int ConsumedCount { get; set; }
    public DateTime? LicenseStart { get; set; }
    public DateTime? LicenseEnd { get; set; }
    // DC-M1: SubscriptionTier dead column removed — BillingController still assigns it
    // (Routes agent handles that cleanup); the [Obsolete] enum is retained until then.
    public BillingStatus BillingStatus { get; set; } = BillingStatus.Active;
    // Stripe billing
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? PastDueAt { get; set; }

    // Branding & notification defaults
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? DefaultAudioSetting { get; set; }
    public ToastScenario DefaultScenario { get; set; } = ToastScenario.Default;

    // Per-tenant content moderation policy.
    // When ModerationEnabled is false, ContentSafetyService short-circuits to Pass
    // for both text and image. Thresholds use Azure Content Safety severity scale (0..6):
    //   - ModerationReviewSeverity: scores >= this go to PendingReview (admin must approve)
    //   - ModerationBlockSeverity:  scores >= this are rejected outright (422)
    // ModerationRequireApprovalAll forces every notification to PendingReview regardless
    // of moderation engine output — used by tenants that require human-in-the-loop for
    // every outgoing notification.
    // ModerationCustomEndpoint/Key let a tenant bring their own Azure Content Safety
    // resource; when null the platform-default ContentSafety:Endpoint/Key from config
    // is used. Both must be set together to take effect.
    public bool ModerationEnabled { get; set; } = true;
    public bool ModerationScanText { get; set; } = true;
    public bool ModerationScanImages { get; set; } = true;
    public int ModerationReviewSeverity { get; set; } = 2;
    public int ModerationBlockSeverity { get; set; } = 5;
    public bool ModerationRequireApprovalAll { get; set; }
    public string? ModerationCustomEndpoint { get; set; }
    public string? ModerationCustomKey { get; set; }
    public string? ModerationBlockedMessage { get; set; }

    // M12 — Device appearance. Two independent fleet-branding surfaces the agent
    // applies at startup/reconnect: a layered click-through desktop info overlay
    // and a per-user lock screen image. Flat columns (same shape as Moderation*)
    // so the appearance-config query path stays a single row read, no JSON blob.
    //   DesktopOverlayFields:   pipe-delimited field keys, e.g. "hostname|user|os".
    //   DesktopOverlayPosition: bottom-right | bottom-left | top-right | top-left.
    // The overlay never reads or writes the user's wallpaper (Keith's directive);
    // it is a separate window painted above the wallpaper, below apps/icons.
    public bool DesktopOverlayEnabled { get; set; }
    public string? DesktopOverlayFields { get; set; }
    public string? DesktopOverlayPosition { get; set; }
    public string? DesktopOverlayCustomText { get; set; }
    // 0.4.15 — admin-controlled panel translucency, 10..100 in 5% steps.
    // Default 85 matches the agent's pre-control hardcoded value, so a tenant
    // that never touches the slider keeps the visual they had at upgrade.
    public int DesktopOverlayOpacityPercent { get; set; } = 85;
    public bool LockScreenEnabled { get; set; }
    public string? LockScreenImageUrl { get; set; }
    // DASH-L1: server-provided cache-bust — stamped whenever the lock-screen image changes,
    // surfaced to agents/dashboard as the ?v= so a replaced image is re-fetched everywhere.
    public DateTime? LockScreenImageUpdatedAt { get; set; }

    // ─── M14 — Microsoft SSO (Entra / Azure AD) ──────────────────────────────
    // Per-tenant federation opt-in. A tenant admin enables Microsoft sign-in by
    // saving their Entra Directory (tenant) ID here. SSO sign-ins are gated on
    // the incoming id_token "tid" claim matching AzureAdTenantId AND SsoEnabled
    // — a valid Microsoft token from ANY other directory is rejected at the
    // callback. This opt-in is the load-bearing gate that keeps the multitenant
    // Entra app from being an open door: any work/school account on the planet
    // can authenticate against the app, but only mapped, opted-in directories
    // resolve to a tenant and get a session.
    //   AzureAdTenantId: the customer's Entra Directory (tenant) GUID (lowercase).
    //   SsoEnabled:      master switch for Microsoft sign-in on this tenant.
    //   SsoRequireMfa:   when true, the id_token must assert MFA (amr contains
    //                    "mfa") or the sign-in is rejected — for orgs that want
    //                    proof Entra enforced a second factor. Off by default;
    //                    we trust the customer's Conditional Access policy.
    public bool SsoEnabled { get; set; }
    public string? AzureAdTenantId { get; set; }
    public bool SsoRequireMfa { get; set; }

    // Tenant-wide native MFA enforcement (distinct from SsoRequireMfa, which only
    // governs the Microsoft SSO id_token). When true, every member must have an
    // authenticator (TOTP) enrolled, and sensitive actions — sending a toast and
    // changing the lock screen — require a fresh step-up MFA verification. Off by
    // default so existing tenants are unaffected. An admin cannot turn this on
    // until their own account has MFA enrolled (self-lockout guard in TenantController).
    public bool RequireMfa { get; set; }

    // Platform Admin controls. Suspension blocks login and device registration
    // without deleting tenant data — reversible. Complimentary marks the tenant
    // as free-forever (no Stripe required, no device cap, LicenseEnd ignored);
    // overrides the trial / free-tier / paid logic in LicenseService.
    public DateTime? SuspendedAt { get; set; }
    public string? SuspendedReason { get; set; }
    public bool IsComplimentary { get; set; }
    public string? ComplimentaryReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<DeviceGroup> DeviceGroups { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
