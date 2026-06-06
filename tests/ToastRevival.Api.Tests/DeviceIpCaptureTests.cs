using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// M1 (Device IP Capture &amp; Display) — backend integration tests for the WAN/LAN
/// IP capture on registration and ping, plus the DeviceResponse projection.
///
/// Runs against the same real Postgres-backed stack as
/// <see cref="EndToEndNotificationTests"/> (Testcontainers, or the
/// TOAST_TEST_CONNECTION_STRING env override). The migration hook applies
/// <c>20260605150506_AddDeviceIpAddresses</c> on host boot, so these assert the
/// columns exist and the controller writes them.
///
/// Note on WAN value under TestServer: the in-process TestServer does not set
/// <c>HttpContext.Connection.RemoteIpAddress</c>, and no CF-Connecting-IP /
/// X-Forwarded-For header is trusted (the null peer is neither a Cloudflare
/// egress nor loopback). <see cref="ToastRevival.Api.Services.CloudflareIpValidator.ResolveTrustedClientIp"/>
/// therefore falls back to its <c>"anon"</c> sentinel. We assert WAN is
/// non-empty (the capture path ran and persisted), not a specific address —
/// the real-IP-vs-Cloudflare-edge behavior is verified in production, not here.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class DeviceIpCaptureTests
{
    private readonly LoadFixture _load;

    public DeviceIpCaptureTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task Register_NewDevice_PersistsWanIp_AndNullLanWhenNotSent()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);

        // Old-agent registration: no lanIpAddress in the payload.
        var deviceResp = await http.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(
            TenantId: auth.TenantId,
            DeviceName: "IP-LAB-01",
            Username: "ip-user",
            OsVersion: "Windows 11 26100",
            AgentVersion: "0.4.0.0"));
        Assert.Equal(HttpStatusCode.OK, deviceResp.StatusCode);
        var device = await deviceResp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(device);

        var row = await GetDeviceRowAsync(factory, device!.DeviceId);
        Assert.False(string.IsNullOrEmpty(row.WanIpAddress)); // server-derived — captured
        Assert.Null(row.LanIpAddress);                        // no agent value yet (M2)
    }

    [Fact]
    public async Task Register_NewDevice_WithLanPayload_PersistsLan()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);

        // New-agent (M2) registration carries the LAN IP.
        var deviceResp = await http.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(
            TenantId: auth.TenantId,
            DeviceName: "IP-LAB-02",
            Username: "ip-user",
            OsVersion: "Windows 11 26100",
            AgentVersion: "0.4.40.0",
            EnrollmentKey: null,
            LanIpAddress: "192.168.1.50"));
        Assert.Equal(HttpStatusCode.OK, deviceResp.StatusCode);
        var device = await deviceResp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(device);

        var row = await GetDeviceRowAsync(factory, device!.DeviceId);
        Assert.False(string.IsNullOrEmpty(row.WanIpAddress));
        Assert.Equal("192.168.1.50", row.LanIpAddress);
    }

    [Fact]
    public async Task Ping_WithLanPayload_RefreshesWan_AndUpdatesLan()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);
        var device = await RegisterDeviceAsync(http, auth.TenantId, "IP-PING-01");

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", device.Token);

        var pingResp = await http.PostAsJsonAsync("/api/devices/ping",
            new PingRequest(AgentVersion: "0.4.40.0", LanIpAddress: "10.0.0.42"));
        Assert.Equal(HttpStatusCode.NoContent, pingResp.StatusCode);

        var row = await GetDeviceRowAsync(factory, device.DeviceId);
        Assert.False(string.IsNullOrEmpty(row.WanIpAddress)); // refreshed on heartbeat
        Assert.Equal("10.0.0.42", row.LanIpAddress);
        Assert.Equal("0.4.40.0", row.AgentVersion);
    }

    [Fact]
    public async Task Ping_OldFormatBody_NoLanField_Returns204_AndDoesNotClearStoredLan()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);
        var device = await RegisterDeviceAsync(http, auth.TenantId, "IP-PING-02");

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", device.Token);

        // First, a new-agent ping sets a LAN value.
        var seedResp = await http.PostAsJsonAsync("/api/devices/ping",
            new PingRequest(AgentVersion: "0.4.40.0", LanIpAddress: "172.16.5.5"));
        Assert.Equal(HttpStatusCode.NoContent, seedResp.StatusCode);

        // Then an OLD agent's heartbeat: raw body with only agentVersion, no
        // lanIpAddress field at all. Must deserialize cleanly (no 400) and must
        // NOT null out the previously captured LAN.
        var oldBody = new StringContent(
            "{\"agentVersion\":\"0.4.26\"}", Encoding.UTF8, "application/json");
        var oldResp = await http.PostAsync("/api/devices/ping", oldBody);
        Assert.Equal(HttpStatusCode.NoContent, oldResp.StatusCode);

        var row = await GetDeviceRowAsync(factory, device.DeviceId);
        Assert.Equal("172.16.5.5", row.LanIpAddress); // preserved, not cleared
        Assert.Equal("0.4.26", row.AgentVersion);     // agentVersion still updates
    }

    [Fact]
    public async Task Ping_EmptyBody_Returns204_AndDoesNotClearStoredLan()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);
        var device = await RegisterDeviceAsync(http, auth.TenantId, "IP-PING-03");

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", device.Token);

        var seedResp = await http.PostAsJsonAsync("/api/devices/ping",
            new PingRequest(AgentVersion: "0.4.40.0", LanIpAddress: "192.168.9.9"));
        Assert.Equal(HttpStatusCode.NoContent, seedResp.StatusCode);

        // Pre-0.4.26 agents send no body at all. WAN still refreshes server-side;
        // LAN is preserved.
        var noBodyResp = await http.PostAsync("/api/devices/ping", content: null);
        Assert.Equal(HttpStatusCode.NoContent, noBodyResp.StatusCode);

        var row = await GetDeviceRowAsync(factory, device.DeviceId);
        Assert.Equal("192.168.9.9", row.LanIpAddress);
        Assert.False(string.IsNullOrEmpty(row.WanIpAddress));
    }

    [Fact]
    public async Task ListDevices_ProjectsWanAndLanIpFields()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;
        using var http = factory.CreateClient();

        var auth = await RegisterTenantAsync(factory);
        var device = await RegisterDeviceAsync(http, auth.TenantId, "IP-LIST-01",
            lanIpAddress: "192.168.7.7");

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AdminToken);

        var devices = await http.GetFromJsonAsync<List<DeviceResponse>>("/api/devices");
        var listed = Assert.Single(devices!, d => d.DeviceId == device.DeviceId);
        Assert.False(string.IsNullOrEmpty(listed.WanIpAddress));
        Assert.Equal("192.168.7.7", listed.LanIpAddress);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task<SecurityHarness.SeededPenTenant> RegisterTenantAsync(ApiTestFactory factory) =>
        SecurityHarness.SeedTenantAsync(factory);

    private static async Task<DeviceTokenResponse> RegisterDeviceAsync(
        HttpClient http, Guid tenantId, string deviceName, string? lanIpAddress = null)
    {
        var resp = await http.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(
            TenantId: tenantId,
            DeviceName: deviceName,
            Username: $"user-{deviceName}",
            OsVersion: "Windows 11 26100",
            AgentVersion: "0.4.40.0",
            EnrollmentKey: null,
            LanIpAddress: lanIpAddress));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var device = await resp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(device);
        return device!;
    }

    private static async Task<Device> GetDeviceRowAsync(ApiTestFactory factory, Guid deviceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Devices.IgnoreQueryFilters().FirstAsync(d => d.Id == deviceId);
    }
}
