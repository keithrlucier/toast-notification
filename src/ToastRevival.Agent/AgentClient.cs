using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Windows.AppNotifications;

namespace ToastRevival.Agent;

/// <summary>
/// First-run device registration via REST.
/// </summary>
internal static class RegistrationService
{
    public static async Task<DeviceConfig?> RegisterAsync(BootstrapConfig bootstrap, CancellationToken ct)
    {
        DiagLog.Write($"Registration: tenantId={bootstrap.TenantId}; serverUrl={bootstrap.ServerUrl}");

        using var http = new HttpClient { BaseAddress = new Uri(bootstrap.ServerUrl) };

        var request = new
        {
            tenantId    = bootstrap.TenantId,
            deviceName  = Environment.MachineName,
            username    = Environment.UserName,
            osVersion   = Environment.OSVersion.VersionString,
            agentVersion = ThisAssembly.Version,
        };

        try
        {
            var response = await http.PostAsJsonAsync("/api/devices/register", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                DiagLog.Write($"Registration failed {(int)response.StatusCode}: {body}");
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken: ct);
            if (dto is null)
            {
                DiagLog.Write("Registration succeeded but response body was null.");
                return null;
            }

            DiagLog.Write($"Registration OK: deviceId={dto.DeviceId}");
            return new DeviceConfig(
                bootstrap.TenantId, bootstrap.ServerUrl,
                dto.DeviceId, dto.Token, dto.SigningKey);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"Registration exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private record DeviceTokenResponse(string Token, Guid DeviceId, Guid TenantId, string SigningKey);
}

/// <summary>
/// Holds a long-running HubConnection. Renders incoming notifications,
/// reports delivery and interaction back through the hub. Heartbeat ping is
/// best-effort liveness in addition to the hub-OnConnectedAsync LastPing touch.
/// </summary>
internal sealed class AgentHubClient : IAsyncDisposable
{
    private readonly DeviceConfig _config;
    private readonly HubConnection _hub;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _pingLoop;

    public AgentHubClient(DeviceConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerUrl),
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", config.DeviceToken) },
        };

        var hubUrl = new Uri(new Uri(config.ServerUrl), "/hubs/notifications").ToString();

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult<string?>(_config.DeviceToken);
            })
            // Reconnect intervals match Anthony's M2 plan: 0, 2, 5, 10, 30 seconds.
            // After 30s the SignalR client keeps trying at the last interval.
            .WithAutomaticReconnect(new TimeSpan[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
            })
            .Build();

        _hub.On<string, string>("ReceiveNotification", OnReceiveNotificationAsync);
        _hub.Reconnecting += ex =>
        {
            DiagLog.Write($"Hub reconnecting: {ex?.GetType().Name}: {ex?.Message}");
            return Task.CompletedTask;
        };
        _hub.Reconnected += id =>
        {
            DiagLog.Write($"Hub reconnected: connectionId={id}");
            return Task.CompletedTask;
        };
        _hub.Closed += ex =>
        {
            DiagLog.Write($"Hub closed: {ex?.GetType().Name}: {ex?.Message}");
            return Task.CompletedTask;
        };

        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        DiagLog.Write($"Hub starting: deviceId={_config.DeviceId}");
        await _hub.StartAsync(ct);
        DiagLog.Write($"Hub started: state={_hub.State}; connectionId={_hub.ConnectionId}");

        _pingLoop = Task.Run(() => RunPingLoopAsync(_shutdown.Token));
    }

    public async ValueTask DisposeAsync()
    {
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;

        _shutdown.Cancel();
        if (_pingLoop is not null)
        {
            try { await _pingLoop; } catch { /* shutdown */ }
        }

        try { await _hub.DisposeAsync(); } catch { /* best-effort */ }
        _http.Dispose();
        _shutdown.Dispose();
    }

    private async Task OnReceiveNotificationAsync(string payloadJson, string signature)
    {
        if (!HmacVerifier.Verify(payloadJson, signature, _config.SigningKey))
        {
            DiagLog.Write("ReceiveNotification: HMAC verification FAILED — payload dropped.");
            return;
        }

        var payload = HmacVerifier.TryDeserialize(payloadJson);
        if (payload is null)
        {
            DiagLog.Write("ReceiveNotification: payload deserialization failed after HMAC pass — payload dropped.");
            return;
        }

        try
        {
            var notification = ToastTemplateBuilder.BuildFromPayload(payload);
            AppNotificationManager.Default.Show(notification);
            DiagLog.Write($"ReceiveNotification: rendered notificationId={payload.NotificationId}; title='{payload.Title}'");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ReceiveNotification: render failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // Report delivery via hub. If the hub call fails (transient disconnect during
        // reconnect window), the event is lost — backend M2.B catch-up endpoint will
        // surface this on next reconnect.
        try
        {
            await _hub.InvokeAsync("ReportDelivery", payload.NotificationId);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ReportDelivery failed for {payload.NotificationId}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // args.Argument is a key=value;key=value string — same shape AppNotificationBuilder.AddArgument produced.
        var parsed = ParseToastArguments(args.Argument);
        DiagLog.Write($"NotificationInvoked: argument='{args.Argument}'");

        if (parsed.TryGetValue("source", out var source) && source == "hub"
            && parsed.TryGetValue("notificationId", out var idStr)
            && Guid.TryParse(idStr, out var notificationId))
        {
            var action = parsed.GetValueOrDefault("action") ?? "click";
            try
            {
                await _hub.InvokeAsync("ReportInteraction", notificationId, action);
                DiagLog.Write($"ReportInteraction: notificationId={notificationId}; action={action}");
            }
            catch (Exception ex)
            {
                DiagLog.Write($"ReportInteraction failed for {notificationId}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task RunPingLoopAsync(CancellationToken ct)
    {
        // Hub OnConnectedAsync already updates Device.LastPing on every reconnect.
        // This loop is the belt-and-suspenders heartbeat for the case where the
        // hub stays cleanly connected for hours and the server hasn't seen any
        // signal. 30-minute cadence: 48 calls/day, well under device-per-hour 10/hr.
        var interval = TimeSpan.FromMinutes(30);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                using var resp = await _http.PostAsync("/api/devices/ping", content: null, ct);
                DiagLog.Write($"Ping: {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                DiagLog.Write($"Ping failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static Dictionary<string, string> ParseToastArguments(string argument)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(argument)) return dict;

        foreach (var pair in argument.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = pair[..idx];
            var value = pair[(idx + 1)..];
            dict[key] = value;
        }
        return dict;
    }
}

/// <summary>
/// Stateless REST poster used by the activation-handler exit path. Posts a single
/// interaction event without standing up a SignalR connection, then exits.
/// </summary>
internal static class InteractionFallback
{
    public static async Task<bool> PostAsync(DeviceConfig config, Guid notificationId, string action, CancellationToken ct)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerUrl),
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", config.DeviceToken) },
        };

        try
        {
            var resp = await http.PostAsJsonAsync(
                $"/api/notifications/{notificationId}/interactions",
                new { action },
                ct);
            DiagLog.Write($"InteractionFallback POST {notificationId}: {(int)resp.StatusCode}");
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"InteractionFallback POST {notificationId} failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}

internal static class ThisAssembly
{
    public static string Version =>
        typeof(ThisAssembly).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
