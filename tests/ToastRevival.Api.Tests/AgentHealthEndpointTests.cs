using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// POST /api/agent/health/{tenantId} — the machine-level liveness ping from the
/// SYSTEM ToastNotificationHealth service. It must ONLY refresh LastPing on device
/// rows that already exist for (tenant, machine): never insert (no seat), never
/// resurrect a removed device, never cross tenants, and never show a suspended
/// tenant's devices online. Runs against the same Postgres-backed stack as the
/// other integration tests.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class AgentHealthEndpointTests
{
    private readonly LoadFixture _load;

    public AgentHealthEndpointTests(LoadFixture load) => _load = load;

    private sealed record HealthResult(int Updated);

    private static readonly DateTime Stale = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Health_RefreshesLastPing_AndWan_OnMatchingActiveRow()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);
        var id = await SeedDeviceAsync(factory, t.TenantId, "HEALTH-PC-01", "alice", DeviceStatus.Active, Stale);

        var resp = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("HEALTH-PC-01"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        var row = await GetRowAsync(factory, id);
        Assert.NotNull(row.LastPing);
        Assert.True(row.LastPing! > Stale, "LastPing should have advanced");
        Assert.False(string.IsNullOrEmpty(row.WanIpAddress)); // server-derived, captured
        Assert.Equal("0.4.40.0", row.AgentVersion);            // NOT touched by the health ping
    }

    [Fact]
    public async Task Health_UpdatesAllRows_ForAMachineSharedByMultipleUsers()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);
        var a = await SeedDeviceAsync(factory, t.TenantId, "SHARED-PC", "alice", DeviceStatus.Active, Stale);
        var b = await SeedDeviceAsync(factory, t.TenantId, "SHARED-PC", "bob",   DeviceStatus.Active, Stale);

        var resp = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("SHARED-PC"));
        Assert.Equal(2, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        Assert.True((await GetRowAsync(factory, a)).LastPing! > Stale);
        Assert.True((await GetRowAsync(factory, b)).LastPing! > Stale);
    }

    [Fact]
    public async Task Health_SkipsDecommissionedAndPendingUninstall_NeverResurrects()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);
        var dec = await SeedDeviceAsync(factory, t.TenantId, "GONE-PC", "alice", DeviceStatus.Decommissioned, Stale);
        var pen = await SeedDeviceAsync(factory, t.TenantId, "GONE-PC", "bob",   DeviceStatus.PendingUninstall, Stale);

        var resp = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("GONE-PC"));
        Assert.Equal(0, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        Assert.Equal(Stale, (await GetRowAsync(factory, dec)).LastPing);
        Assert.Equal(Stale, (await GetRowAsync(factory, pen)).LastPing);
    }

    [Fact]
    public async Task Health_NoMatchingRow_Returns200_Updated0_AndCreatesNoDevice()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);

        var resp = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("NEVER-REGISTERED-PC"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        Assert.Equal(0, await CountDevicesAsync(factory, t.TenantId)); // never inserts -> no seat
    }

    [Fact]
    public async Task Health_SuspendedTenant_DoesNotMarkDevicesOnline()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);
        var id = await SeedDeviceAsync(factory, t.TenantId, "SUSP-PC", "alice", DeviceStatus.Active, Stale);
        await MutateTenantAsync(factory, t.TenantId, t => t.SuspendedAt = DateTime.UtcNow);

        var resp = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("SUSP-PC"));
        Assert.Equal(0, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        Assert.Equal(Stale, (await GetRowAsync(factory, id)).LastPing); // untouched
    }

    [Fact]
    public async Task Health_CrossTenant_LeavesOtherTenantsDeviceUntouched()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var a = await SecurityHarness.SeedTenantAsync(factory);
        var b = await SecurityHarness.SeedTenantAsync(factory);
        var idA = await SeedDeviceAsync(factory, a.TenantId, "DUP-NAME-PC", "alice", DeviceStatus.Active, Stale);

        // Ping tenant B with the SAME machine name — must not touch tenant A's device.
        var resp = await http.PostAsJsonAsync($"/api/agent/health/{b.TenantId}",
            new MachineHealthRequest("DUP-NAME-PC"));
        Assert.Equal(0, (await resp.Content.ReadFromJsonAsync<HealthResult>())!.Updated);

        Assert.Equal(Stale, (await GetRowAsync(factory, idA)).LastPing);
    }

    [Fact]
    public async Task Health_LegacyEnrollmentKey_GatesOnMatch()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var t = await SecurityHarness.SeedTenantAsync(factory);
        var id = await SeedDeviceAsync(factory, t.TenantId, "KEYED-PC", "alice", DeviceStatus.Active, Stale);
        await MutateTenantAsync(factory, t.TenantId, t => t.EnrollmentKey = "s3cret-key");

        // Wrong/missing key -> no-op.
        var bad = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("KEYED-PC", EnrollmentKey: "wrong"));
        Assert.Equal(0, (await bad.Content.ReadFromJsonAsync<HealthResult>())!.Updated);
        Assert.Equal(Stale, (await GetRowAsync(factory, id)).LastPing);

        // Correct key -> updates.
        var ok = await http.PostAsJsonAsync($"/api/agent/health/{t.TenantId}",
            new MachineHealthRequest("KEYED-PC", EnrollmentKey: "s3cret-key"));
        Assert.Equal(1, (await ok.Content.ReadFromJsonAsync<HealthResult>())!.Updated);
        Assert.True((await GetRowAsync(factory, id)).LastPing! > Stale);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedDeviceAsync(
        ApiTestFactory factory, Guid tenantId, string deviceName, string username,
        DeviceStatus status, DateTime lastPing)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var d = new Device
        {
            TenantId          = tenantId,
            DeviceName        = deviceName,
            Username          = username,
            OsVersion         = "Windows 11 26100",
            AgentVersion      = "0.4.40.0",
            RegistrationToken = $"health-test-{Guid.NewGuid():n}",
            Status            = status,
            LastPing          = lastPing,
        };
        db.Devices.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    private static async Task MutateTenantAsync(ApiTestFactory factory, Guid tenantId, Action<Tenant> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = await db.Tenants.IgnoreQueryFilters().FirstAsync(x => x.Id == tenantId);
        mutate(t);
        await db.SaveChangesAsync();
    }

    private static async Task<Device> GetRowAsync(ApiTestFactory factory, Guid deviceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Devices.IgnoreQueryFilters().FirstAsync(d => d.Id == deviceId);
    }

    private static async Task<int> CountDevicesAsync(ApiTestFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Devices.IgnoreQueryFilters().CountAsync(d => d.TenantId == tenantId);
    }
}
