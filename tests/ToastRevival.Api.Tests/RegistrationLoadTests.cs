using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// M8.C — closes INFO-M8B-002 (registration-path load scenario).
///
/// The M8.B load harness pre-seeds devices via DB scope to keep the test
/// focused on hub fanout, deliberately skipping <c>POST /api/devices/register</c>.
/// That leaves the registration path itself unexercised under concurrency —
/// which is where the meaningful failure modes live: the unique-index on
/// <c>RegistrationToken</c>, concurrent <c>SaveChangesAsync</c> against the
/// licensing counters, and the StripeBillingSyncService no-op contract for
/// tenants without a real Stripe subscription.
///
/// This test stands up its own <see cref="ApiTestFactory"/> against the
/// shared <see cref="PostgresFixture"/> for two reasons:
///   1. The <c>device-per-hour</c> rate-limit policy partitions on
///      <c>deviceId</c> claim or <c>RemoteIpAddress?.ToString() ?? "anon"</c>.
///      Unauthenticated registrations bucket to the same <c>"anon"</c>
///      partition (TestServer's RemoteIpAddress is null), so a fresh factory
///      = a fresh rate-limit window. Sharing the load fixture's factory would
///      leak budget from prior tests.
///   2. The factory owns the in-memory NotificationQueueService and rate
///      limiter; tearing both down with the test isolates the shared fixture
///      from concurrent-registration churn.
///
/// Default-skipped — opt-in via <c>TOAST_TEST_RUN_REGISTRATION_LOAD=1</c>.
/// Same gating pattern M8.B established for the 1,000-device fanout variant.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class RegistrationLoadTests
{
    /// <summary>
    /// Concurrent registration count. Sized below the
    /// <c>device-per-hour</c> rate limit (10/hr) for a single
    /// <c>RemoteIpAddress?.ToString() ?? "anon"</c> partition, leaving a
    /// 2-call cushion for retry / pre-flight if the test ever issues one.
    /// </summary>
    private const int ConcurrentRegistrationCount = 8;

    private const string OptInEnvVar = "TOAST_TEST_RUN_REGISTRATION_LOAD";

    // xUnit 2 cannot wire one collection fixture into another's constructor,
    // so the collection exposes only LoadFixture; PostgresFixture is reached
    // via LoadFixture.Postgres (same Docker container, same lifecycle).
    private readonly PostgresFixture _postgres;

    public RegistrationLoadTests(LoadFixture loadFixture)
    {
        _postgres = loadFixture.Postgres;
    }

    [Fact]
    [Trait("category", "registration-load")]
    public async Task Registration_ConcurrentBurst_AllSucceed_NoCollisions_ConsumedCountAccurate()
    {
        if (Environment.GetEnvironmentVariable(OptInEnvVar) != "1")
        {
            // Opt-in: this scenario steals device-per-hour budget on the
            // shared TestServer "anon" partition; not safe to run on every
            // CI cadence yet (INFO-M8B-002 carry-forward).
            return;
        }

        await using var factory = new ApiTestFactory(_postgres.ConnectionString);

        // Force the host to boot so migrations apply and DI is live before
        // the first concurrent registration touches the DB.
        using (var warmup = factory.CreateClient())
        {
            await warmup.GetAsync("/api/templates");
        }

        var tenantId = await SeedTenantAsync(factory);

        var registrations = new Task<HttpResponseMessage>[ConcurrentRegistrationCount];
        for (int i = 0; i < ConcurrentRegistrationCount; i++)
        {
            var index = i;
            registrations[index] = Task.Run(async () =>
            {
                using var http = factory.CreateClient();
                var req = new RegisterDeviceRequest(
                    TenantId:     tenantId,
                    DeviceName:   $"reg-load-{index:D5}",
                    Username:     $"reg-user-{index:D5}",
                    OsVersion:    "Windows 11 26100",
                    AgentVersion: "0.4.0.0");
                return await http.PostAsJsonAsync("/api/devices/register", req);
            });
        }

        var responses = await Task.WhenAll(registrations);

        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

            var deviceIds = new HashSet<Guid>();
            foreach (var resp in responses)
            {
                var body = await resp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
                Assert.NotNull(body);
                Assert.True(deviceIds.Add(body!.DeviceId),
                    $"Duplicate DeviceId returned: {body.DeviceId} — registration token uniqueness or transaction isolation broke.");
                Assert.Equal(tenantId, body.TenantId);
                Assert.False(string.IsNullOrEmpty(body.SigningKey));
            }

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Tenant.ConsumedCount is incremented inside the controller after
            // the device row commits. Concurrent registrations must accumulate
            // accurately — a lost write would surface here.
            var tenant = await db.Tenants.IgnoreQueryFilters()
                .FirstAsync(t => t.Id == tenantId);
            Assert.Equal(ConcurrentRegistrationCount, tenant.ConsumedCount);

            // Each device row landed under the seeded tenant.
            var registeredDeviceCount = await db.Devices.IgnoreQueryFilters()
                .Where(d => d.TenantId == tenantId)
                .CountAsync();
            Assert.Equal(ConcurrentRegistrationCount, registeredDeviceCount);
        }
        finally
        {
            foreach (var resp in responses) resp.Dispose();
        }
    }

    private async Task<Guid> SeedTenantAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var slug = Guid.NewGuid().ToString("n");
        var tenant = new Tenant
        {
            Name          = $"RegLoad {slug}",
            Subdomain     = $"regload-{slug[..16]}",
            SigningKey    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            BillingStatus = BillingStatus.Trialing,
            // No StripeSubscriptionId — StripeBillingSyncService.SyncSubscriptionQuantityAsync
            // early-returns on null/empty so the test never reaches Stripe.
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }
}
