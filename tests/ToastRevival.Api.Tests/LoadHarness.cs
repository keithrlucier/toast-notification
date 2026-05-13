using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Harness for the M8.B load deliverable (D4). Seeds a tenant + N devices
/// directly via the DB scope (skipping the rate-limited
/// <c>/api/devices/register</c> endpoint, which is covered by M8.A's E2E test
/// and would cap an unauth'd burst at the device-per-hour anon partition),
/// opens N concurrent SignalR LongPolling connections, posts one notification
/// targeting all N devices, and measures per-device receive latency.
///
/// Why pre-seed instead of registering through the public API: M8.A already
/// proves the registration path. M8.B is exercising the fanout path —
/// queue → hub group → device — which is exactly what the
/// <see cref="ToastRevival.Api.Services.NotificationQueueService"/>
/// background service does in production. Seeding bypasses authentication and
/// rate-limiting code that isn't on the load path.
///
/// Why LongPolling: the in-process TestServer doesn't speak WebSockets. Payload
/// signing and delivery semantics are transport-agnostic, so LongPolling is a
/// faithful exercise of the producer-side code. WebSocket-transport variant
/// is INFO-M8A-003 (M8.C).
/// </summary>
internal static class LoadHarness
{
    /// <summary>
    /// Seeded test artifact. Carries the admin JWT (for posting notifications)
    /// plus per-device JWT tokens so the harness can open authenticated hub
    /// connections without round-tripping the registration endpoint.
    /// </summary>
    public sealed record SeededTenant(
        Guid TenantId,
        string SigningKey,
        string AdminToken,
        IReadOnlyList<SeededDevice> Devices);

    public sealed record SeededDevice(Guid DeviceId, string Token);

    /// <summary>
    /// One run of the fanout harness. Latency values are measured from the
    /// moment <c>POST /api/notifications</c> is dispatched (the API-caller's
    /// view of "send started") to the moment each device's
    /// <c>ReceiveNotification</c> handler fires (the agent's view of "delivery
    /// arrived"). This includes API request handling, queue enqueue, queue
    /// consumer dequeue, EF round-trip to load + flip Notification + Tenant,
    /// payload signing, and per-delivery hub send.
    /// </summary>
    public sealed record FanoutRunResult(
        int DeviceCount,
        int Received,
        int VerifyFailures,
        TimeSpan TotalElapsed,
        double FirstReceiveMs,
        double LastReceiveMs,
        double P50Ms,
        double P95Ms,
        double P99Ms);

    public static async Task<SeededTenant> SeedTenantAsync(
        ApiTestFactory factory,
        int deviceCount,
        string tenantNamePrefix = "Load")
    {
        if (deviceCount <= 0) throw new ArgumentOutOfRangeException(nameof(deviceCount));

        using var scope = factory.Services.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager  = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var tenantSlug = Guid.NewGuid().ToString("n");
        var tenant = new Tenant
        {
            Name          = $"{tenantNamePrefix} {tenantSlug}",
            // Subdomain has a uniqueness constraint at the DB level; keep it short
            // and unique per seed call so multiple harness runs don't collide
            // when the Respawner is unavailable.
            Subdomain     = $"load-{tenantSlug[..16]}",
            SigningKey    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            BillingStatus = BillingStatus.Trialing,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var adminEmail = $"admin-{Guid.NewGuid():n}@load.test";
        var admin = new AppUser
        {
            UserName       = adminEmail,
            Email          = adminEmail,
            EmailConfirmed = true,
            TenantId       = tenant.Id,
            // >100-device targets require Admin+ at the controller gate
            // (NotificationsController.Send line 65), so the seeded admin must
            // be at least Admin to drive a real fanout test.
            Role           = UserRole.Admin,
        };
        var createResult = await userManager.CreateAsync(admin, "LoadPass!2026");
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to seed admin user: {errors}");
        }

        var adminToken = tokenService.CreateUserToken(admin);

        // Bulk insert devices in a single SaveChanges. EF translates this to
        // a single INSERT...VALUES batch on Npgsql, which is fast for 1k rows.
        var devices = new List<Device>(deviceCount);
        for (int i = 0; i < deviceCount; i++)
        {
            devices.Add(new Device
            {
                TenantId          = tenant.Id,
                DeviceName        = $"load-dev-{i:D5}",
                Username          = $"load-user-{i:D5}",
                OsVersion         = "Windows 11 26100",
                AgentVersion      = "0.4.0.0",
                // Real registration uses a SHA-256 hash of a fresh random
                // token; the load harness only needs the device row to exist
                // and is authenticated via TokenService-minted JWT, so a
                // sentinel value is correct here.
                RegistrationToken = $"load-harness-not-jwt-path-{Guid.NewGuid():n}",
                Status            = DeviceStatus.Active,
            });
        }
        db.Devices.AddRange(devices);
        await db.SaveChangesAsync();

        var seededDevices = devices
            .Select(d => new SeededDevice(d.Id, tokenService.CreateDeviceToken(d)))
            .ToList();

        return new SeededTenant(tenant.Id, tenant.SigningKey, adminToken, seededDevices);
    }

    public static async Task<FanoutRunResult> RunSingleNotificationFanoutAsync(
        ApiTestFactory factory,
        SeededTenant tenant,
        TimeSpan receiveTimeout)
    {
        var deviceCount = tenant.Devices.Count;
        // -1 sentinel: device hasn't received yet. Stopwatch.GetTimestamp() lets
        // us avoid the ticks-vs-frequency confusion of TimeSpan.FromTicks on a
        // raw stopwatch elapsed; we capture timestamps and convert at the end.
        var receiveTicks    = new long[deviceCount];
        var verifyFailures  = 0;
        Array.Fill(receiveTicks, -1L);

        Uri hubUrl;
        using (var probe = factory.CreateClient())
        {
            hubUrl = new Uri(probe.BaseAddress!, "/hubs/notifications");
        }

        var connections = new HubConnection[deviceCount];
        for (int i = 0; i < deviceCount; i++)
        {
            var device = tenant.Devices[i];
            var conn = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.Transports                = HttpTransportType.LongPolling;
                    opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    opts.AccessTokenProvider       = () => Task.FromResult<string?>(device.Token);
                })
                .Build();
            connections[i] = conn;
        }

        try
        {
            // Wire up handlers BEFORE StartAsync — SignalR's contract requires
            // On<>() registration prior to connection start, and registering
            // late means missing the first server-to-client invocation.
            for (int i = 0; i < deviceCount; i++)
            {
                var idx = i;
                connections[i].On<string, string>("ReceiveNotification", (payloadJson, signature) =>
                {
                    var nowTicks = Stopwatch.GetTimestamp();
                    if (!PayloadVerifier.Verify(payloadJson, signature, tenant.SigningKey))
                        Interlocked.Increment(ref verifyFailures);
                    Interlocked.Exchange(ref receiveTicks[idx], nowTicks);
                });
            }

            // Open all connections in parallel. Task.WhenAll is fine — the
            // TestServer's request pipeline serves each negotiate/connect via
            // its in-memory handler factory.
            await Task.WhenAll(connections.Select(c => c.StartAsync()));

            using var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tenant.AdminToken);

            var sendReq = new SendNotificationRequest(
                Title:      "M8.B fanout load test",
                BodyLine1:  $"Targeting {deviceCount} devices",
                BodyLine2:  null,
                Scenario:   ToastScenario.Default,
                TargetType: TargetType.Device,
                TargetIds:  tenant.Devices.Select(d => d.DeviceId).ToList());

            // Capture the dispatch timestamp at the moment of POST initiation;
            // the server's queue-then-fanout pipeline fires inside the await.
            var dispatchTicks = Stopwatch.GetTimestamp();
            var resp          = await http.PostAsJsonAsync("/api/notifications", sendReq);
            if (resp.StatusCode != HttpStatusCode.Accepted)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"POST /api/notifications returned {(int)resp.StatusCode} {resp.StatusCode}: {body}");
            }

            // Drain to all-received or timeout.
            var deadlineTicks =
                Stopwatch.GetTimestamp() + (long)(receiveTimeout.TotalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadlineTicks)
            {
                var pending = 0;
                for (int i = 0; i < deviceCount; i++)
                    if (Volatile.Read(ref receiveTicks[i]) == -1L)
                        pending++;
                if (pending == 0) break;
                await Task.Delay(20);
            }

            var receivedTicks = receiveTicks.Where(t => t >= 0).ToArray();
            var received      = receivedTicks.Length;
            var totalElapsed  = received > 0
                ? TicksToTimeSpan(receivedTicks.Max() - dispatchTicks)
                : receiveTimeout;

            var latenciesMs = receivedTicks
                .Select(t => TicksToMs(t - dispatchTicks))
                .OrderBy(x => x)
                .ToArray();

            return new FanoutRunResult(
                DeviceCount:    deviceCount,
                Received:       received,
                VerifyFailures: verifyFailures,
                TotalElapsed:   totalElapsed,
                FirstReceiveMs: latenciesMs.Length > 0 ? latenciesMs[0]                                       : 0,
                LastReceiveMs:  latenciesMs.Length > 0 ? latenciesMs[^1]                                      : 0,
                P50Ms:          Percentile(latenciesMs, 0.50),
                P95Ms:          Percentile(latenciesMs, 0.95),
                P99Ms:          Percentile(latenciesMs, 0.99));
        }
        finally
        {
            // Stop in parallel; per-connection timeouts are bounded by SignalR.
            await Task.WhenAll(connections.Select(async c =>
            {
                try { await c.StopAsync(); }
                catch { /* fan-in cleanup; surface failures via the harness result */ }
                await c.DisposeAsync();
            }));
        }
    }

    private static double Percentile(double[] sortedAsc, double q)
    {
        if (sortedAsc.Length == 0) return 0;
        // Nearest-rank percentile — adequate for harness reporting; the test
        // assertions key off coarse budgets, not statistical precision.
        var idx = (int)Math.Ceiling(q * sortedAsc.Length) - 1;
        if (idx < 0) idx = 0;
        if (idx >= sortedAsc.Length) idx = sortedAsc.Length - 1;
        return sortedAsc[idx];
    }

    private static TimeSpan TicksToTimeSpan(long stopwatchTicks)
        => TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

    private static double TicksToMs(long stopwatchTicks)
        => (double)stopwatchTicks * 1000.0 / Stopwatch.Frequency;
}
