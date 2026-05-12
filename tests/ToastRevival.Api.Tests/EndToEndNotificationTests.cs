using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// First end-to-end integration test for the M8 milestone — exercises the
/// full register → device-register → admin-send → SignalR-fanout →
/// HMAC-verify → ReportDelivery → ReportInteraction loop against a real
/// PostgreSQL container (or an env-configured Postgres) with the real Identity,
/// JWT, SignalR, and hosted NotificationQueueService stack.
///
/// What this proves:
/// 1. <c>POST /api/auth/register</c> creates a tenant + admin user + signing key.
/// 2. <c>POST /api/devices/register</c> issues a device JWT and surfaces the
///    tenant signing key to the agent.
/// 3. The SignalR hub honors the device JWT and joins the device-{id} group.
/// 4. <c>POST /api/notifications</c> persists Notification + Pending Delivery,
///    enqueues, and the hosted queue fans out a signed payload to the device
///    group via the hub.
/// 5. The signed payload survives the wire intact — its HMAC verifies under
///    the same key the agent received at registration.
/// 6. <c>ReportDelivery</c> over the hub flips the Delivery row to Delivered.
/// 7. <c>ReportInteraction</c> over the hub flips it to Clicked with the action
///    string and an InteractedAt timestamp.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class EndToEndNotificationTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollTimeout    = TimeSpan.FromSeconds(10);

    private readonly LoadFixture _load;

    public EndToEndNotificationTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task HubFanout_DeliversSignedPayload_ReportsDelivery_ReportsInteraction()
    {
        // Reset the shared database to a clean slate so the seeded tenant
        // doesn't collide with a prior test run's leftovers (Respawner is a
        // no-op on connection strings that don't support DDL truncation).
        await _load.ResetAsync();

        var factory    = _load.Factory;
        using var http = factory.CreateClient();

        // 1) Register the tenant + admin user — first session this DB has seen.
        var registerEmail = $"admin-{Guid.NewGuid():n}@toastrevival.test";
        var registerReq = new RegisterRequest(
            TenantName: $"E2E Tenant {Guid.NewGuid():n}",
            Email: registerEmail,
            Password: "TestPass123!");

        var registerResp = await http.PostAsJsonAsync("/api/auth/register", registerReq);
        Assert.Equal(HttpStatusCode.OK, registerResp.StatusCode);
        var auth = await registerResp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // 2) Register a device — unauthenticated endpoint, returns the device
        // JWT and the tenant signing key the agent will HMAC-verify against.
        var deviceReq = new RegisterDeviceRequest(
            TenantId: auth!.TenantId,
            DeviceName: "E2E-LAB-01",
            Username: "lab-user",
            OsVersion: "Windows 11 26100",
            AgentVersion: "0.4.0.0");

        var deviceResp = await http.PostAsJsonAsync("/api/devices/register", deviceReq);
        Assert.Equal(HttpStatusCode.OK, deviceResp.StatusCode);
        var device = await deviceResp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(device);
        Assert.False(string.IsNullOrEmpty(device!.SigningKey));

        // 3) Open a SignalR hub connection on the device JWT. Force LongPolling
        // because the in-process TestServer doesn't speak WebSockets — the
        // payload-signing and ReportDelivery / ReportInteraction methods are
        // transport-agnostic, so this is a faithful exercise of the agent loop.
        var hubUrl = new Uri(http.BaseAddress!, "/hubs/notifications");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.Transports = HttpTransportType.LongPolling;
                opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                opts.AccessTokenProvider = () => Task.FromResult<string?>(device.Token);
            })
            .Build();

        var receivedTcs = new TaskCompletionSource<(string PayloadJson, string Signature)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<string, string>("ReceiveNotification",
            (payloadJson, signature) => receivedTcs.TrySetResult((payloadJson, signature)));

        await connection.StartAsync();

        try
        {
            // 4) Send a notification targeted at this device. Single-device
            // target keeps the test off the broadcast/MFA path.
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", auth.Token);

            var sendReq = new SendNotificationRequest(
                Title: "E2E test notification",
                BodyLine1: "Integration test from M8.A",
                BodyLine2: "Round-trip: send → SignalR → verify → report",
                Scenario: ToastScenario.Default,
                TargetType: TargetType.Device,
                TargetIds: new List<Guid> { device.DeviceId });

            var sendResp = await http.PostAsJsonAsync("/api/notifications", sendReq);
            Assert.Equal(HttpStatusCode.Accepted, sendResp.StatusCode);
            var notification = await sendResp.Content.ReadFromJsonAsync<NotificationResponse>();
            Assert.NotNull(notification);
            var notificationId = notification!.Id;

            // 5) Wait for the hosted NotificationQueueService to fan out via
            // the hub. ReceiveNotification should fire with the same byte
            // sequence the server signed.
            var received = await WithTimeout(receivedTcs.Task, ReceiveTimeout,
                "Did not receive ReceiveNotification within timeout — hub fanout may be broken.");

            Assert.False(string.IsNullOrEmpty(received.PayloadJson));
            Assert.False(string.IsNullOrEmpty(received.Signature));
            Assert.True(
                PayloadVerifier.Verify(received.PayloadJson, received.Signature, device.SigningKey),
                "HMAC signature did not verify against the tenant signing key.");
            Assert.Contains(notificationId.ToString(), received.PayloadJson);

            // 6) ReportDelivery — flip Pending → Delivered.
            await connection.SendAsync("ReportDelivery", notificationId);
            await PollUntil(factory, async db =>
            {
                var d = await db.NotificationDeliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.NotificationId == notificationId);
                return d?.Status == DeliveryStatus.Delivered && d.DeliveredAt != null;
            }, "NotificationDelivery did not advance to Delivered after ReportDelivery.");

            // 7) ReportInteraction — Delivered → Clicked, Action recorded.
            const string action = "acknowledge";
            await connection.SendAsync("ReportInteraction", notificationId, action);
            await PollUntil(factory, async db =>
            {
                var d = await db.NotificationDeliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.NotificationId == notificationId);
                return d?.Status == DeliveryStatus.Clicked
                    && d.InteractedAt != null
                    && d.Action == action;
            }, "NotificationDelivery did not advance to Clicked after ReportInteraction.");

            // 8) Final invariants — single delivery row, parent Notification
            // closed in a terminal Sent state.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var deliveries = await db.NotificationDeliveries.IgnoreQueryFilters()
                .Where(d => d.NotificationId == notificationId)
                .ToListAsync();
            var single = Assert.Single(deliveries);
            Assert.Equal(device.DeviceId, single.DeviceId);
            Assert.Equal(auth.TenantId, single.TenantId);
            Assert.Equal(action, single.Action);

            var parent = await db.Notifications.IgnoreQueryFilters()
                .FirstAsync(n => n.Id == notificationId);
            Assert.Equal(NotificationStatus.Sent, parent.Status);
            Assert.NotNull(parent.SentAt);
            Assert.NotNull(parent.CompletedAt);
        }
        finally
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeviceGroups_CreateMembershipAndTargeting_ScopesToSelectedGroup()
    {
        await _load.ResetAsync();

        var factory    = _load.Factory;
        using var http = factory.CreateClient();

        var registerEmail = $"groups-{Guid.NewGuid():n}@toastrevival.test";
        var registerReq = new RegisterRequest(
            TenantName: $"Group Tenant {Guid.NewGuid():n}",
            Email: registerEmail,
            Password: "TestPass123!");

        var registerResp = await http.PostAsJsonAsync("/api/auth/register", registerReq);
        Assert.Equal(HttpStatusCode.OK, registerResp.StatusCode);
        var auth = await registerResp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        var serverDeviceResp = await http.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(
            TenantId: auth!.TenantId,
            DeviceName: "GROUP-SERVER-01",
            Username: "server-user",
            OsVersion: "Windows Server 2025",
            AgentVersion: "0.4.0.0"));
        Assert.Equal(HttpStatusCode.OK, serverDeviceResp.StatusCode);
        var serverDevice = await serverDeviceResp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(serverDevice);

        var workstationDeviceResp = await http.PostAsJsonAsync("/api/devices/register", new RegisterDeviceRequest(
            TenantId: auth.TenantId,
            DeviceName: "GROUP-WORKSTATION-01",
            Username: "workstation-user",
            OsVersion: "Windows 11 26100",
            AgentVersion: "0.4.0.0"));
        Assert.Equal(HttpStatusCode.OK, workstationDeviceResp.StatusCode);
        var workstationDevice = await workstationDeviceResp.Content.ReadFromJsonAsync<DeviceTokenResponse>();
        Assert.NotNull(workstationDevice);

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        var createGroupResp = await http.PostAsJsonAsync("/api/devicegroups", new CreateDeviceGroupRequest(
            Name: "Servers",
            Description: "Critical infrastructure"));
        Assert.Equal(HttpStatusCode.Created, createGroupResp.StatusCode);
        var group = await createGroupResp.Content.ReadFromJsonAsync<DeviceGroupResponse>();
        Assert.NotNull(group);

        var setMembersResp = await http.PutAsJsonAsync(
            $"/api/devicegroups/{group!.Id}/members",
            new SetDeviceGroupMembersRequest(new List<Guid> { serverDevice!.DeviceId }));
        Assert.Equal(HttpStatusCode.NoContent, setMembersResp.StatusCode);

        var groups = await http.GetFromJsonAsync<List<DeviceGroupResponse>>("/api/devicegroups");
        var listedGroup = Assert.Single(groups!);
        Assert.Equal(group.Id, listedGroup.Id);
        Assert.Equal(1, listedGroup.DeviceCount);

        var devices = await http.GetFromJsonAsync<List<DeviceResponse>>("/api/devices");
        var serverRow = Assert.Single(devices!, d => d.DeviceId == serverDevice.DeviceId);
        var workstationRow = Assert.Single(devices!, d => d.DeviceId == workstationDevice!.DeviceId);
        Assert.Contains(group.Id, serverRow.GroupIds);
        Assert.DoesNotContain(group.Id, workstationRow.GroupIds);

        var sendReq = new SendNotificationRequest(
            Title: "Group target test",
            BodyLine1: "Only the server group should receive this",
            Scenario: ToastScenario.Default,
            TargetType: TargetType.Group,
            TargetIds: new List<Guid> { group.Id });

        var sendResp = await http.PostAsJsonAsync("/api/notifications", sendReq);
        Assert.Equal(HttpStatusCode.Accepted, sendResp.StatusCode);
        var notification = await sendResp.Content.ReadFromJsonAsync<NotificationResponse>();
        Assert.NotNull(notification);
        Assert.Equal(1, notification!.TargetDeviceCount);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = Assert.Single(await db.NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.NotificationId == notification.Id)
            .ToListAsync());
        Assert.Equal(serverDevice.DeviceId, delivery.DeviceId);
        Assert.Equal(auth.TenantId, delivery.TenantId);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string failureMessage)
    {
        var winner = await Task.WhenAny(task, Task.Delay(timeout));
        if (winner != task)
            throw new TimeoutException(failureMessage);
        return await task;
    }

    private static async Task PollUntil(
        ApiTestFactory factory,
        Func<AppDbContext, Task<bool>> predicate,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await predicate(db)) return;
            await Task.Delay(100);
        }
        throw new TimeoutException(failureMessage);
    }
}
