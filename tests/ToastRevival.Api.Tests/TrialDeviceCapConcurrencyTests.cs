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
/// Regression test for a TOCTOU race between
/// <c>LicenseService.CanRegisterDeviceAsync</c> and the device INSERT in
/// <c>DevicesController.Register</c>. Before the fix two concurrent
/// <c>POST /api/devices/register</c> calls for the same trial tenant could
/// both pass the 2-device gate (each saw <c>ConsumedCount</c> below the cap),
/// then both commit, leaving the tenant with 3 devices on a 2-device trial.
///
/// The fix moves check + insert + ConsumedCount increment into a single
/// transaction in <c>LicenseService.TryRegisterDeviceAtomicAsync</c>, gated
/// by a per-tenant <c>pg_advisory_xact_lock</c>. This test fires two
/// concurrent registrations at "cap minus one" and asserts exactly one
/// succeeds — proving the lock serializes them and the second sees the
/// authoritative count.
///
/// Spins up its own <see cref="ApiTestFactory"/> with
/// <c>TOAST_REQUIRE_BILLING=true</c>: the trial cap is only enforced when
/// billing is required, which is off by default in tests (matches the
/// self-host config used by <see cref="appsettings.Test.json"/>).
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class TrialDeviceCapConcurrencyTests
{
    // xUnit 2 cannot wire one collection fixture into another's constructor,
    // so the collection exposes only LoadFixture; PostgresFixture is reached
    // via LoadFixture.Postgres (same Docker container, same lifecycle).
    private readonly PostgresFixture _postgres;

    public TrialDeviceCapConcurrencyTests(LoadFixture loadFixture)
    {
        _postgres = loadFixture.Postgres;
    }

    [Fact]
    public async Task Register_ConcurrentTrialBurst_NeverExceedsTrialCap()
    {
        await using var factory = new ApiTestFactory(
            _postgres.ConnectionString,
            extraConfig: new Dictionary<string, string?>
            {
                // Without this the cap short-circuits to true and the race
                // is moot — TOAST_REQUIRE_BILLING governs the bypass.
                ["TOAST_REQUIRE_BILLING"] = "true",
            });

        // Force the host to boot so migrations apply before the first request.
        using (var warmup = factory.CreateClient())
        {
            await warmup.GetAsync("/api/templates");
        }

        var tenantId = await SeedTrialTenantAsync(factory);

        // Step 1: register one device sequentially — establishes the
        // "cap minus one" state (ConsumedCount=1, trial cap=2).
        using (var first = await PostRegisterAsync(factory, tenantId, "device-pre"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        // Step 2: fire two concurrent registers. Without the advisory lock both
        // would pass the gate and create a third row. With the fix, exactly one
        // wins; the other sees the updated ConsumedCount under the lock and
        // returns 403.
        var raceA = Task.Run(() => PostRegisterAsync(factory, tenantId, "device-race-A"));
        var raceB = Task.Run(() => PostRegisterAsync(factory, tenantId, "device-race-B"));
        var responses = await Task.WhenAll(raceA, raceB);

        try
        {
            var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            var rejectedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden);

            Assert.Equal(1, okCount);
            Assert.Equal(1, rejectedCount);

            // Step 3: authoritative DB state — exactly 2 devices, ConsumedCount=2.
            // A regressed implementation would land here at 3/3.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenant = await db.Tenants.IgnoreQueryFilters()
                .FirstAsync(t => t.Id == tenantId);
            Assert.Equal(2, tenant.ConsumedCount);

            var deviceCount = await db.Devices.IgnoreQueryFilters()
                .Where(d => d.TenantId == tenantId)
                .CountAsync();
            Assert.Equal(2, deviceCount);
        }
        finally
        {
            foreach (var resp in responses) resp.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> PostRegisterAsync(
        ApiTestFactory factory, Guid tenantId, string deviceName)
    {
        using var http = factory.CreateClient();
        var req = new RegisterDeviceRequest(
            TenantId:     tenantId,
            DeviceName:   deviceName,
            Username:     $"user-{deviceName}",
            OsVersion:    "Windows 11 26100",
            AgentVersion: "0.4.0.0");
        return await http.PostAsJsonAsync("/api/devices/register", req);
    }

    private static async Task<Guid> SeedTrialTenantAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var slug = Guid.NewGuid().ToString("n");
        var tenant = new Tenant
        {
            Name          = $"TrialCap {slug}",
            Subdomain     = $"trialcap-{slug[..16]}",
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
