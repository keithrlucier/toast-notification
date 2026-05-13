using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// WebSocket-transport hub variant test. The E2E suite and the fanout load
/// harness both force <see cref="HttpTransportType.LongPolling"/> because
/// TestServer's default <c>HttpMessageHandlerFactory</c> can't speak
/// WebSockets. This test uses <c>factory.Server.CreateWebSocketClient()</c>
/// to exercise the WebSocket handshake path — specifically the query-string
/// JWT extraction in <c>JwtBearerEvents.OnMessageReceived</c> reading
/// <c>access_token</c> from the query when the request path starts with
/// <c>/hubs</c>.
///
/// Production agents use SignalR's default transport negotiation, which
/// settles on WebSockets when both ends support it. The query-string JWT
/// path is the seam SignalR's WebSocket transport relies on (browsers can't
/// set custom headers on a WS upgrade), so a regression in that one event
/// handler would silently break every production agent's hub authentication.
///
/// <see cref="HttpTransportType.WebSockets"/> with <c>SkipNegotiation = true</c>
/// drives the client straight at the hub URL — no negotiate POST, no
/// Authorization header — so the only authentication channel is the
/// <c>access_token</c> query string.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class WebSocketTransportTests
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

    private readonly LoadFixture _load;

    public WebSocketTransportTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task WebSocket_HubAuthenticatesViaQueryStringAccessToken_AndReceivesNotification()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        var t = await SecurityHarness.SeedTenantAsync(factory, role: UserRole.Admin, deviceCount: 1);
        var device = t.Devices[0];

        Uri hubUrl;
        using (var probe = factory.CreateClient())
        {
            hubUrl = new Uri(probe.BaseAddress!, "/hubs/notifications");
        }

        var wsClient = factory.Server.CreateWebSocketClient();

        // SkipNegotiation + Transports=WebSockets means the SignalR client
        // hits the hub URL directly with a WebSocket upgrade and no
        // Authorization header. The only auth channel is access_token in
        // the query string — exactly the path JwtBearerEvents.OnMessageReceived
        // is responsible for picking up.
        //
        // When a custom WebSocketFactory is provided, the SignalR client does
        // NOT automatically append ?access_token= to ctx.Uri (that only happens
        // in DefaultWebSocketFactory which adds it as an Authorization header
        // instead). We must append it manually so OnMessageReceived sees it.
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.Transports        = HttpTransportType.WebSockets;
                opts.SkipNegotiation   = true;
                opts.AccessTokenProvider = () => Task.FromResult<string?>(device.Token);
                opts.WebSocketFactory  = async (ctx, ct) =>
                {
                    var token = await opts.AccessTokenProvider!();
                    var uriStr = ctx.Uri.AbsoluteUri;
                    var sep = uriStr.Contains('?') ? "&" : "?";
                    var connectUri = new Uri($"{uriStr}{sep}access_token={Uri.EscapeDataString(token ?? "")}");
                    return await wsClient.ConnectAsync(connectUri, ct);
                };
            })
            .Build();

        var receivedTcs = new TaskCompletionSource<(string PayloadJson, string Signature)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<string, string>("ReceiveNotification",
            (payloadJson, signature) => receivedTcs.TrySetResult((payloadJson, signature)));

        await connection.StartAsync();

        try
        {
            // Post a notification targeting this device — the hosted
            // NotificationQueueService fans out via the hub. If the WS
            // handshake didn't pick up the access_token, OnConnectedAsync
            // wouldn't have run (Authorize would 401 the upgrade) and the
            // device would never have joined the device-{id} group, so
            // ReceiveNotification would never fire.
            using var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", t.AdminToken);

            var sendReq = new SendNotificationRequest(
                Title:      "ws-transport probe",
                BodyLine1:  "via query-string JWT",
                Scenario:   ToastScenario.Default,
                TargetType: TargetType.Device,
                TargetIds:  new List<Guid> { device.DeviceId });

            var sendResp = await http.PostAsJsonAsync("/api/notifications", sendReq);
            sendResp.EnsureSuccessStatusCode();

            var winner = await Task.WhenAny(receivedTcs.Task, Task.Delay(ReceiveTimeout));
            Assert.True(winner == receivedTcs.Task,
                "ReceiveNotification did not fire — JWT bearer query-string path may be broken.");

            var (payloadJson, signature) = await receivedTcs.Task;
            Assert.False(string.IsNullOrEmpty(payloadJson));
            Assert.False(string.IsNullOrEmpty(signature));
            Assert.True(
                PayloadVerifier.Verify(payloadJson, signature, t.SigningKey),
                "HMAC verify failed against tenant signing key on the WebSocket-delivered payload.");
        }
        finally
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
        }
    }
}
