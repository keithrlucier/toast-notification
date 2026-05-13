using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;
using Xunit.Abstractions;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Load testing harness exercising the fanout path (POST → queue → hub →
/// device receive) at concurrency. Default test runs at 100 devices on every
/// CI push; the 1,000-device test is opt-in via the
/// <c>TOAST_TEST_RUN_LOAD_1K=1</c> environment variable so the CI runner
/// stays predictable in wall time and Linux file-descriptor pressure.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class LoadTests
{
    private const int    DefaultDeviceCount = 100;
    private const int    OptInLargeCount    = 1000;
    private const string OptInEnvKey        = "TOAST_TEST_RUN_LOAD_1K";

    private readonly LoadFixture        _load;
    private readonly ITestOutputHelper  _output;

    public LoadTests(LoadFixture load, ITestOutputHelper output)
    {
        _load   = load;
        _output = output;
    }

    /// <summary>
    /// Default load test — 100 concurrent agents on a single tenant receive
    /// the same signed notification within a 30-second budget. Asserts:
    ///   1. Every device receives the notification.
    ///   2. Every received payload HMAC-verifies under the tenant signing key.
    ///   3. p95 latency under 5s — generous because TestServer LongPolling
    ///      is slower than production WebSocket; this is a behavioral
    ///      smoke, not a SLA gate. Adjust the budget per data captured on
    ///      the CI runner once the test has a few green runs.
    /// </summary>
    [Fact]
    public async Task Fanout_To_DefaultDeviceCount_DeliversWithinLatencyBudget()
    {
        await _load.ResetAsync();
        var tenant = await LoadHarness.SeedTenantAsync(_load.Factory, DefaultDeviceCount);
        var result = await LoadHarness.RunSingleNotificationFanoutAsync(
            _load.Factory, tenant, TimeSpan.FromSeconds(30));

        EmitReport(result);

        Assert.Equal(DefaultDeviceCount, result.Received);
        Assert.Equal(0, result.VerifyFailures);
        Assert.True(
            result.P95Ms < 5000,
            $"p95 latency {result.P95Ms:F0}ms exceeds 5000ms budget for {DefaultDeviceCount} devices");
    }

    /// <summary>
    /// Opt-in 1,000-device variant. Set <c>TOAST_TEST_RUN_LOAD_1K=1</c> to run.
    /// Skipped by default to keep the CI runner under wall-time and to spare
    /// the Linux file-descriptor pool — 1k SignalR LongPolling connections
    /// against an in-process TestServer is loaded.
    ///
    /// When opted in, asserts:
    ///   1. ≥ 99% of devices receive within a 2-minute budget (allows up to
    ///      10 stragglers from TestServer queueing pressure — production
    ///      WebSocket fanout doesn't have the same in-process contention).
    ///   2. Zero HMAC verify failures across received payloads.
    /// </summary>
    [Fact]
    [Trait("category", "load-1k")]
    public async Task Fanout_To_LargeCount_OptIn_DeliversWithinLooseBudget()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInEnvKey);
        if (optIn != "1")
        {
            _output.WriteLine($"Skipped: set {OptInEnvKey}=1 to run the 1,000-device fanout.");
            return;
        }

        await _load.ResetAsync();
        var tenant = await LoadHarness.SeedTenantAsync(_load.Factory, OptInLargeCount);
        var result = await LoadHarness.RunSingleNotificationFanoutAsync(
            _load.Factory, tenant, TimeSpan.FromMinutes(2));

        EmitReport(result);

        var minimumReceived = (int)Math.Ceiling(OptInLargeCount * 0.99);
        Assert.True(
            result.Received >= minimumReceived,
            $"Only {result.Received}/{OptInLargeCount} devices received within budget (need ≥ {minimumReceived})");
        Assert.Equal(0, result.VerifyFailures);
    }

    /// <summary>
    /// Queue-saturation behavior: 5 notifications dispatched in tight
    /// succession against a 30-device tenant. Verifies the unbounded
    /// <c>Channel&lt;Guid&gt;</c> in <c>NotificationQueueService</c> drains
    /// cleanly without message loss when the producer outpaces the consumer.
    /// The assertion is that every notification reaches <c>Sent</c> status —
    /// <c>ProcessAsync</c> only flips to <c>Sent</c> when every per-delivery
    /// hub send succeeds, so this is end-to-end completeness, not just queue
    /// dequeue.
    /// </summary>
    [Fact]
    public async Task Sustained_Burst_AllNotificationsDrainCleanly()
    {
        await _load.ResetAsync();

        const int Devices            = 30;
        const int NotificationCount  = 5;

        var tenant = await LoadHarness.SeedTenantAsync(_load.Factory, Devices);

        using var http = _load.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tenant.AdminToken);

        var ids = new List<Guid>();
        for (int i = 0; i < NotificationCount; i++)
        {
            var sendReq = new SendNotificationRequest(
                Title:      $"Saturation burst {i}",
                BodyLine1:  $"Burst slot {i}",
                Scenario:   ToastScenario.Default,
                TargetType: TargetType.Device,
                TargetIds:  tenant.Devices.Select(d => d.DeviceId).ToList());

            var resp = await http.PostAsJsonAsync("/api/notifications", sendReq);
            Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<NotificationResponse>();
            Assert.NotNull(body);
            ids.Add(body!.Id);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _load.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var statuses = await db.Notifications
                .IgnoreQueryFilters()
                .Where(n => ids.Contains(n.Id))
                .Select(n => n.Status)
                .ToListAsync();

            if (statuses.Count == NotificationCount && statuses.All(s => s == NotificationStatus.Sent))
                return;
            if (statuses.Any(s => s == NotificationStatus.Failed || s == NotificationStatus.PartialFailure))
                Assert.Fail(
                    $"Burst saturation produced Failed/PartialFailure rows: {string.Join(",", statuses)}");
            await Task.Delay(100);
        }

        Assert.Fail("Notifications did not all reach Sent within 60s — burst queue did not drain cleanly.");
    }

    private void EmitReport(LoadHarness.FanoutRunResult r)
    {
        _output.WriteLine($"[fanout] devices={r.DeviceCount} received={r.Received} verifyFailures={r.VerifyFailures}");
        _output.WriteLine($"[fanout] elapsed={r.TotalElapsed.TotalMilliseconds:F0}ms first={r.FirstReceiveMs:F0}ms last={r.LastReceiveMs:F0}ms");
        _output.WriteLine($"[fanout] p50={r.P50Ms:F0}ms p95={r.P95Ms:F0}ms p99={r.P99Ms:F0}ms");
    }
}
