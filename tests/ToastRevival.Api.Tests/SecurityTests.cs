using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Security pen-test surface. Probes across four lanes: tenant isolation,
/// auth bypass, content injection, privilege escalation. All tests share the
/// <see cref="LoadFixture"/> for factory amortization; each test calls
/// <c>_load.ResetAsync()</c> first to start from an empty schema. Tests that
/// target a controller-level guard (rate limit, gate, MFA) seed the minimum
/// data the guard needs and assert the response without exercising adjacent
/// paths.
///
/// One regression caught here: AuditController List/Export were not scoping
/// to the caller's tenantId, leaking cross-tenant audit rows to any tenant
/// admin. <see cref="TenantIsolation_AuditList_DoesNotLeakOtherTenantsRows"/>
/// would have failed before the fix landed.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class SecurityTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    private readonly LoadFixture _load;

    public SecurityTests(LoadFixture load)
    {
        _load = load;
    }

    // ─── Defensive Response Headers ──────────────────────────────────────────

    [Fact]
    public async Task SecurityDefaults_ResponseIncludesDefensiveHeaders()
    {
        // Closes the "no security headers" gap — Program.cs sets
        // X-Content-Type-Options, X-Frame-Options, Referrer-Policy, and
        // Permissions-Policy on every response (including 401 challenges
        // and static-file responses) so a future middleware reorder or
        // accidental short-circuit can't silently strip them.
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        // /api/templates is [Authorize]; unauthenticated GET returns 401.
        // The 401 response must still carry the defensive headers — that's
        // the whole point of placing the middleware before authentication.
        var resp = await http.GetAsync("/api/templates");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        Assert.Equal("nosniff", SingleHeaderValue(resp, "X-Content-Type-Options"));
        Assert.Equal("DENY", SingleHeaderValue(resp, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", SingleHeaderValue(resp, "Referrer-Policy"));

        var permissions = SingleHeaderValue(resp, "Permissions-Policy");
        Assert.Contains("camera=()", permissions);
        Assert.Contains("microphone=()", permissions);
        Assert.Contains("geolocation=()", permissions);
    }

    private static string SingleHeaderValue(HttpResponseMessage resp, string name)
    {
        if (resp.Headers.TryGetValues(name, out var values)) return string.Join(",", values);
        if (resp.Content.Headers.TryGetValues(name, out var contentValues)) return string.Join(",", contentValues);
        throw new InvalidOperationException($"Response did not carry header '{name}'.");
    }

    // ─── Tenant Isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_DeviceList_FiltersToOwnTenant()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 3, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 5, tenantNamePrefix: "Iso-B");

        using var http = SecurityHarness.AuthedClient(factory, a.AdminToken);
        var resp = await http.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var devices = await resp.Content.ReadFromJsonAsync<DeviceResponse[]>();
        Assert.NotNull(devices);
        Assert.Equal(3, devices!.Length);

        var bDeviceIds = b.Devices.Select(d => d.DeviceId).ToHashSet();
        Assert.DoesNotContain(devices, d => bDeviceIds.Contains(d.DeviceId));
    }

    [Fact]
    public async Task TenantIsolation_DeviceGetById_ReturnsNotFoundForOtherTenant()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-B");

        // A's admin trying to read B's device-by-id. The global query filter
        // on Devices is keyed off ITenantProvider — A's tenant filter masks
        // B's row, so the controller's _db.Devices.FindAsync sees nothing.
        using var http = SecurityHarness.AuthedClient(factory, a.AdminToken);
        var resp = await http.GetAsync($"/api/devices/{b.Devices[0].DeviceId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_NotificationSendTargetingOtherTenantsDevices_ResolvesToZero()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 0, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 2, tenantNamePrefix: "Iso-B");

        using var http = SecurityHarness.AuthedClient(factory, a.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      "isolation probe",
            BodyLine1:  "should not deliver",
            BodyLine2:  null,
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.Device,
            TargetIds:  b.Devices.Select(d => d.DeviceId).ToList());

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);

        // The Devices.Where(...) under A's tenant filter resolves zero matches
        // for B's IDs, so ResolveTargetDeviceIds returns empty → 400.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_PendingEndpoint_DeviceFromOtherTenantSeesNothing()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-B");

        // Seed one Pending delivery for A's device — direct DB write, bypasses
        // the queue + moderation path so the test stays focused on the
        // catch-up endpoint's tenant scoping.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = new Notification
            {
                TenantId          = a.TenantId,
                SenderId          = a.AdminUser.Id,
                Title             = "scoped",
                TargetType        = TargetType.Device,
                TargetDeviceCount = 1,
                Status            = NotificationStatus.Sending,
            };
            db.Notifications.Add(notif);
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                NotificationId = notif.Id,
                DeviceId       = a.Devices[0].DeviceId,
                TenantId       = a.TenantId,
            });
            await db.SaveChangesAsync();
        }

        // B's device hits /pending — must see zero items even though one
        // Pending row exists in the same DB (for a different tenant + device).
        using var http = SecurityHarness.AuthedClient(factory, b.Devices[0].Token);
        var resp = await http.GetAsync("/api/notifications/pending");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var items = await resp.Content.ReadFromJsonAsync<PendingNotificationItem[]>();
        Assert.NotNull(items);
        Assert.Empty(items!);
    }

    [Fact]
    public async Task PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds()
    {
        // Explicit ?limit= query param on /api/notifications/pending. Default
        // 100 (backwards compat for v0.3.x agents that omit the param); clamps
        // to [1, 500]. Wire shape stays an array — agents in the field
        // unmarshal `List<PendingNotificationItem>` and need no rebuild.
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Pagination");

        // Seed 510 Notifications, each with one Pending delivery for the
        // tenant's single device. 510 lets all four assertions read the same
        // Pending set without reseeding: default (100), explicit-in-range (200),
        // upper-clamp (limit=999 → 500), lower-clamp (limit=0 → 1).
        // NOTE: NotificationDeliveries has a unique index on (NotificationId,
        // DeviceId), so we cannot create 510 deliveries for one notification —
        // one notification per delivery is required.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (int i = 0; i < 510; i++)
            {
                var notif = new Notification
                {
                    TenantId          = t.TenantId,
                    SenderId          = t.AdminUser.Id,
                    Title             = $"pagination probe {i:D3}",
                    TargetType        = TargetType.Device,
                    TargetDeviceCount = 1,
                    Status            = NotificationStatus.Sending,
                };
                db.Notifications.Add(notif);
                db.NotificationDeliveries.Add(new NotificationDelivery
                {
                    NotificationId = notif.Id,
                    DeviceId       = t.Devices[0].DeviceId,
                    TenantId       = t.TenantId,
                });
            }
            await db.SaveChangesAsync();
        }

        using var http = SecurityHarness.AuthedClient(factory, t.Devices[0].Token);

        var defaultResp = await http.GetAsync("/api/notifications/pending");
        Assert.Equal(HttpStatusCode.OK, defaultResp.StatusCode);
        var defaultItems = await defaultResp.Content.ReadFromJsonAsync<PendingNotificationItem[]>();
        Assert.NotNull(defaultItems);
        Assert.Equal(100, defaultItems!.Length);

        var explicitResp = await http.GetAsync("/api/notifications/pending?limit=200");
        Assert.Equal(HttpStatusCode.OK, explicitResp.StatusCode);
        var explicitItems = await explicitResp.Content.ReadFromJsonAsync<PendingNotificationItem[]>();
        Assert.NotNull(explicitItems);
        Assert.Equal(200, explicitItems!.Length);

        var clampHighResp = await http.GetAsync("/api/notifications/pending?limit=999");
        Assert.Equal(HttpStatusCode.OK, clampHighResp.StatusCode);
        var clampHighItems = await clampHighResp.Content.ReadFromJsonAsync<PendingNotificationItem[]>();
        Assert.NotNull(clampHighItems);
        Assert.Equal(500, clampHighItems!.Length);

        var clampLowResp = await http.GetAsync("/api/notifications/pending?limit=0");
        Assert.Equal(HttpStatusCode.OK, clampLowResp.StatusCode);
        var clampLowItems = await clampLowResp.Content.ReadFromJsonAsync<PendingNotificationItem[]>();
        Assert.NotNull(clampLowItems);
        Assert.Single(clampLowItems!);
    }

    [Fact]
    public async Task TenantIsolation_AuditList_DoesNotLeakOtherTenantsRows()
    {
        // Regression test for cross-tenant audit leak. The AuditLog entity
        // has no global query filter (the PlatformAdmin SystemController
        // needs cross-tenant visibility); without an explicit
        // .Where(l => l.TenantId == tenantId) in the per-tenant
        // AuditController, A's admin sees B's audit rows. The fix scopes
        // both List and Export to the caller's tenantId claim.
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 0, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 0, tenantNamePrefix: "Iso-B");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                TenantId     = a.TenantId,
                UserId       = a.AdminUser.Id,
                Action       = "tenant-a-action",
                ResourceType = "Probe",
            });
            db.AuditLogs.Add(new AuditLog
            {
                TenantId     = b.TenantId,
                UserId       = b.AdminUser.Id,
                Action       = "tenant-b-action",
                ResourceType = "Probe",
            });
            await db.SaveChangesAsync();
        }

        using var http = SecurityHarness.AuthedClient(factory, a.AdminToken);
        var resp = await http.GetAsync("/api/audit?days=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var raw = await resp.Content.ReadAsStringAsync();
        Assert.Contains("tenant-a-action", raw);
        Assert.DoesNotContain("tenant-b-action", raw);
    }

    [Fact]
    public async Task TenantIsolation_HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 1, tenantNamePrefix: "Iso-B");

        Uri hubUrl;
        using (var probe = factory.CreateClient())
        {
            hubUrl = new Uri(probe.BaseAddress!, "/hubs/notifications");
        }

        // Open A's admin connection first — joins tenant-{A.id} group.
        var aAdmin = BuildHubConnection(factory, hubUrl, a.AdminToken);
        var aReceived = new List<Guid>();
        aAdmin.On<Guid>("DeviceConnected", id =>
        {
            lock (aReceived) aReceived.Add(id);
        });
        await aAdmin.StartAsync();

        // Now connect B's device — joins tenant-{B.id} group plus device-{B.id}.
        // A's admin should NOT see this DeviceConnected event because A is in
        // a different tenant group.
        var bDevice = BuildHubConnection(factory, hubUrl, b.Devices[0].Token);
        await bDevice.StartAsync();

        try
        {
            // Predicate-poll: if isolation is broken the bad event arrives almost
            // immediately, so fail fast. Otherwise drain for up to 300ms to be
            // confident nothing leaked through.
            var deadline = DateTime.UtcNow.AddMilliseconds(300);
            while (DateTime.UtcNow < deadline)
            {
                lock (aReceived)
                {
                    if (aReceived.Count > 0) break; // leaked — assertion below will fail
                }
                await Task.Delay(20);
            }

            lock (aReceived)
            {
                Assert.DoesNotContain(b.Devices[0].DeviceId, aReceived);
            }
        }
        finally
        {
            await aAdmin.StopAsync();
            await bDevice.StopAsync();
            await aAdmin.DisposeAsync();
            await bDevice.DisposeAsync();
        }
    }

    // ─── Auth Bypass ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthBypass_ExpiredUserJwt_Returns401()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 0);
        var claims = SecurityHarness.StandardUserClaims(
            t.AdminUser.Id, t.TenantId, t.AdminUser.Email!, UserRole.Admin);

        // ClockSkew is set to TimeSpan.Zero in Program.cs so anything in the
        // past is rejected without grace. Forge with exp 1 minute back-dated.
        var expired = SecurityHarness.ForgeUserJwt(factory, claims, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        using var http = SecurityHarness.AuthedClient(factory, expired);
        var resp = await http.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AuthBypass_JwtSignedWithWrongKey_Returns401()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.SuperAdmin, deviceCount: 0);
        var claims = SecurityHarness.StandardUserClaims(
            t.AdminUser.Id, t.TenantId, t.AdminUser.Email!, UserRole.SuperAdmin);

        // Inject a forged platformAdmin claim and sign with the wrong key —
        // proves the symmetric-key check is the real gate, not the claim
        // surface. JwtBearer rejects before authorization ever sees the claim.
        claims.Add(new Claim("platformAdmin", "true"));
        var forged = SecurityHarness.ForgeUserJwt(
            factory, claims,
            signingKeyOverride: "this-is-not-the-real-key-just-32-bytes-of-noise-padding");

        using var http = SecurityHarness.AuthedClient(factory, forged);
        var resp = await http.GetAsync("/api/system/tenants");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AuthBypass_DeviceJwtMissingDeviceIdClaim_ReturnsUnauthorized_OnPending()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, deviceCount: 0);

        // type=device but no deviceId claim. JwtBearer accepts (signed with
        // the right key, not expired). The controller-level
        // GetPending check fails the type+deviceId+tenantId triple and returns
        // 401 Unauthorized.
        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new("tenantId", t.TenantId.ToString()),
            new("type",     "device"),
        };
        var token = SecurityHarness.ForgeUserJwt(factory, claims);

        using var http = SecurityHarness.AuthedClient(factory, token);
        var resp = await http.GetAsync("/api/notifications/pending");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AuthBypass_UserJwtOnDevicePendingEndpoint_ReturnsUnauthorized()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 0);

        // The admin's user JWT passes the controller-level [Authorize] but the
        // GetPending action checks type=="device" before reading payload data.
        // A user JWT yields 401, even though the bearer middleware accepted it.
        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var resp = await http.GetAsync("/api/notifications/pending");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AuthBypass_BroadcastToAllWithoutMfaClaim_Returns403()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.SuperAdmin, deviceCount: 1);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      "broadcast probe",
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.All);

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("mfa_required", body);
    }

    [Fact]
    public async Task MfaStepUp_TotpEnrolledUser_SmsPathBlocked_ForcesAuthenticator()
    {
        // MFA-7: a user with an enrolled TOTP authenticator must not be able to
        // downgrade step-up elevation to the weaker SMS channel. Both the send and the
        // verify SMS endpoints refuse with 403 { error: "totp_required" }, forcing the
        // authenticator path. The step-up modal treats that 403 as "no SMS available"
        // and falls back to the TOTP code automatically — no frontend change required.
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.SuperAdmin, deviceCount: 0);

        // Enroll the caller in TOTP and give them a confirmed phone, so the only thing
        // standing between them and the SMS path is the new guard.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u  = await db.Users.IgnoreQueryFilters().FirstAsync(x => x.Id == t.AdminUser.Id);
            u.MfaSecret            = "JBSWY3DPEHPK3PXP"; // any non-empty enrolled secret
            u.PhoneNumber          = "+15555550123";
            u.PhoneNumberConfirmed = true;
            await db.SaveChangesAsync();
        }

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);

        // send-sms refuses before generating/sending any ClickSend SMS.
        var sendResp = await http.PostAsync("/api/auth/mfa/send-sms", null);
        Assert.Equal(HttpStatusCode.Forbidden, sendResp.StatusCode);
        Assert.Contains("totp_required", await sendResp.Content.ReadAsStringAsync());

        // verify-sms refuses too (defense in depth — a code minted before enrollment,
        // or a direct API call, still can't elevate a TOTP user over SMS).
        var verifyResp = await http.PostAsJsonAsync("/api/auth/mfa/verify-sms", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Forbidden, verifyResp.StatusCode);
        Assert.Contains("totp_required", await verifyResp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MfaStepUp_NonTotpUser_SmsGuardIsInert()
    {
        // MFA-7 non-breaking guarantee: the TOTP guard must be inert for SMS-only /
        // SSO / legacy users (no MfaSecret). Proven hermetically without an external
        // SMS send — with no enrolled TOTP and no confirmed phone, send-sms falls
        // through the guard to the phone check (400, not 403 totp_required), and
        // verify-sms falls through to the code-expiry check (401, not 403).
        await _load.ResetAsync();
        var factory = _load.Factory;

        // Seeded admin has no MfaSecret and no confirmed phone — the guard must not fire.
        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.SuperAdmin, deviceCount: 0);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);

        var sendResp = await http.PostAsync("/api/auth/mfa/send-sms", null);
        Assert.Equal(HttpStatusCode.BadRequest, sendResp.StatusCode);
        Assert.DoesNotContain("totp_required", await sendResp.Content.ReadAsStringAsync());

        var verifyResp = await http.PostAsJsonAsync("/api/auth/mfa/verify-sms", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, verifyResp.StatusCode);
        Assert.DoesNotContain("totp_required", await verifyResp.Content.ReadAsStringAsync());
    }

    // ─── Content Injection ───────────────────────────────────────────────────

    [Fact]
    public async Task ContentInjection_XssInBody_PersistsRawAndSurvivesSigning()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 1);

        const string xss = "<script>alert(1)</script>";
        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      "xss probe",
            BodyLine1:  xss,
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.Device,
            TargetIds:  new List<Guid> { t.Devices[0].DeviceId });

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        // The server stores raw text; the agent's WinAppSDK XmlDocument escapes
        // when rendering. The contract: the HMAC-signed payload bytes must
        // contain the literal XSS string so the agent verifies, then escapes
        // at render time.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == t.TenantId)
            .OrderByDescending(n => n.CreatedAt)
            .FirstAsync();
        Assert.Equal(xss, stored.BodyLine1);
    }

    [Fact]
    public async Task ContentInjection_OversizedTitle_RejectedByModelValidation()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 1);

        // Title is [MaxLength(64)] on SendNotificationRequest. 10 KB title
        // trips the [ApiController] auto-400 path — never reaches the
        // controller body, never persisted, never queued.
        var huge = new string('A', 10_000);
        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      huge,
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.Device,
            TargetIds:  new List<Guid> { t.Devices[0].DeviceId });

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ContentInjection_UnicodeBoundary_RoundTripsClean()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 1);

        // Emoji + RTL mark + zero-width joiner + null-replacement boundary.
        // The HMAC covers UTF-8 bytes; if the JSON serializer mangles the
        // sequence between persist and signing, the agent verify would fail.
        const string boundary = "alert‍‮�msg \U0001F525";

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      "unicode probe",
            BodyLine1:  boundary,
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.Device,
            TargetIds:  new List<Guid> { t.Devices[0].DeviceId });

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == t.TenantId)
            .OrderByDescending(n => n.CreatedAt)
            .FirstAsync();
        Assert.Equal(boundary, stored.BodyLine1);
    }

    [Fact]
    public async Task ContentInjection_ScriptCloseTagInBody_PersistsRawAndDeliversIntactOverHub()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 1);

        // The </script> sequence is dangerous in HTML JSON-LD contexts (the
        // dashboard surface guards against it separately). On the API path,
        // the payload is signed JSON, never rendered as HTML — but the wire
        // shape must round-trip cleanly so the agent's deserializer doesn't
        // choke.
        // Asserting here on the wire-delivered payload exercises the
        // production NotificationPayloadBuilder.BuildSigned path inside the
        // hosted queue service without exposing internals to the test project.
        const string body = "</script><img src=x onerror=alert(1)>";

        Uri hubUrl;
        using (var probe = factory.CreateClient())
        {
            hubUrl = new Uri(probe.BaseAddress!, "/hubs/notifications");
        }

        var connection = BuildHubConnection(factory, hubUrl, t.Devices[0].Token);
        var receivedTcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string, string>("ReceiveNotification",
            (payloadJson, _) => receivedTcs.TrySetResult(payloadJson));

        await connection.StartAsync();
        try
        {
            using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
            var sendReq = new SendNotificationRequest(
                Title:      "script-close probe",
                BodyLine1:  body,
                Scenario:   ToastScenario.Default,
                TargetType: TargetType.Device,
                TargetIds:  new List<Guid> { t.Devices[0].DeviceId });

            var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
            Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

            var winner = await Task.WhenAny(receivedTcs.Task, Task.Delay(ReceiveTimeout));
            Assert.True(winner == receivedTcs.Task,
                "ReceiveNotification did not fire — payload-building may have failed on </script>.");

            var payloadJson = await receivedTcs.Task;
            var doc         = JsonDocument.Parse(payloadJson);
            Assert.Equal(body, doc.RootElement.GetProperty("bodyLine1").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Notifications.IgnoreQueryFilters()
                .Where(n => n.TenantId == t.TenantId)
                .OrderByDescending(n => n.CreatedAt)
                .FirstAsync();
            Assert.Equal(body, stored.BodyLine1);
        }
        finally
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
        }
    }

    // ─── Privilege Escalation ────────────────────────────────────────────────

    [Fact]
    public async Task PrivilegeEscalation_TechnicianInviteUser_Returns403()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Technician, deviceCount: 0);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var inviteReq = new InviteUserRequest(
            Email:    $"newbie-{Guid.NewGuid():n}@pen.test",
            Password: "NewbiePass!2026",
            Role:     UserRole.Admin);

        var resp = await http.PostAsJsonAsync("/api/users/invite", inviteReq);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PrivilegeEscalation_AdminChangingOwnRole_Returns400()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 0);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var roleReq = new UpdateRoleRequest(UserRole.SuperAdmin);
        var resp    = await http.PutAsJsonAsync($"/api/users/{t.AdminUser.Id}/role", roleReq);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PrivilegeEscalation_AdminTargetingOtherTenantUser_Returns404()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var a = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 0, tenantNamePrefix: "Esc-A");
        var b = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 0, tenantNamePrefix: "Esc-B");

        // A's admin tries to modify B's admin's role. The Users query filter
        // scopes _db.Users.FindAsync to A's tenant; B's user row is invisible,
        // controller returns 404 — privilege confined to home tenant.
        using var http = SecurityHarness.AuthedClient(factory, a.AdminToken);
        var roleReq = new UpdateRoleRequest(UserRole.Technician);
        var resp    = await http.PutAsJsonAsync($"/api/users/{b.AdminUser.Id}/role", roleReq);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PrivilegeEscalation_TechnicianBroadcastOver100Devices_Returns403()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Technician, deviceCount: 101);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var sendReq = new SendNotificationRequest(
            Title:      "broadcast gate probe",
            Scenario:   ToastScenario.Default,
            TargetType: TargetType.Device,
            TargetIds:  t.Devices.Select(d => d.DeviceId).ToList());

        var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("insufficient_role", body);
    }

    [Fact]
    public async Task PrivilegeEscalation_AdminWithoutPlatformAdminClaim_Returns403_OnSystemTenants()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        // SuperAdmin role within the tenant, but IsPlatformAdmin=false → JWT
        // omits the platformAdmin claim → SystemController policy rejects.
        var t = await SecurityHarness.SeedTenantAsync(
            factory, role: UserRole.SuperAdmin, deviceCount: 0, isPlatformAdmin: false);

        using var http = SecurityHarness.AuthedClient(factory, t.AdminToken);
        var resp = await http.GetAsync("/api/system/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static HubConnection BuildHubConnection(ApiTestFactory factory, Uri hubUrl, string token)
    {
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.Transports                = HttpTransportType.LongPolling;
                opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                opts.AccessTokenProvider       = () => Task.FromResult<string?>(token);
            })
            .Build();
    }
}
