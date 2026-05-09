using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Memory;
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
/// Wire shape returned by GET /api/notifications/pending. PayloadJson + Signature
/// are the same byte sequence the hub fanout would have pushed via
/// "ReceiveNotification", so the agent runs the exact same HMAC verify + render
/// path regardless of which channel delivered the payload.
/// </summary>
internal sealed record PendingNotificationItem(
    Guid NotificationId,
    string PayloadJson,
    string Signature,
    DateTime CreatedAt);

/// <summary>
/// Holds a long-running HubConnection. Renders incoming notifications,
/// reports delivery and interaction back through the hub. Heartbeat ping is
/// best-effort liveness in addition to the hub-OnConnectedAsync LastPing touch.
/// </summary>
internal sealed class AgentHubClient : IAsyncDisposable
{
    /// <summary>
    /// Sliding-expiration window for the notificationId de-dup cache (M2.B,
    /// INFO-M2A-004). SignalR can redeliver buffered ReceiveNotification
    /// messages after a reconnect, and the catch-up endpoint can serve a
    /// notification the hub already pushed in the same connection — both
    /// paths share this cache so a notification is rendered + acknowledged
    /// at most once per hour-long sliding window.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(1);

    private readonly DeviceConfig _config;
    private readonly HubConnection _hub;
    private readonly HttpClient _http;
    private readonly MemoryCache _renderedCache = new(new MemoryCacheOptions());
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _pingLoop;

    /// <summary>
    /// Lower bound for the next catch-up call. Null on first run so the
    /// initial GET drains the full backlog — the most common scenario is an
    /// agent that rebooted and reconnects with Pending deliveries that
    /// pre-date this process. After each successful catch-up, set to UtcNow
    /// so subsequent calls only fetch deliveries created since the last
    /// drain.
    ///
    /// FIX-M2B-001 (caught by Abish in Code Sweep): initializing this to
    /// UtcNow at ctor time would have made the first call exclude every
    /// pre-existing Pending delivery (CreatedAt &lt; ctor_time), defeating
    /// the very milestone shipping. Stay null on first call.
    /// </summary>
    private DateTime? _lastCatchupSince = null;

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
        _hub.Reconnected += async id =>
        {
            DiagLog.Write($"Hub reconnected: connectionId={id}");
            // M2.B: drain anything sent while we were disconnected. Fire-and-forget
            // semantics — do not block the SignalR client's event pump.
            try
            {
                await RunCatchupAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagLog.Write($"Catch-up after Reconnected failed: {ex.GetType().Name}: {ex.Message}");
            }
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

        // Cold-start catch-up: cover anything dispatched while the agent was
        // down (process exit, machine asleep, network out). The hub will only
        // push notifications sent AFTER OnConnectedAsync put us in the
        // device-{id} group; this fills the gap.
        try
        {
            await RunCatchupAsync(ct);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"Catch-up after StartAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
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
        _renderedCache.Dispose();
        _shutdown.Dispose();
    }

    private async Task OnReceiveNotificationAsync(string payloadJson, string signature)
    {
        await RenderAndReportAsync(payloadJson, signature, source: "hub");
    }

    /// <summary>
    /// Fetch every Pending delivery for this device since the last known
    /// good window and run them through the same verify+render+report
    /// pipeline as live hub messages. Idempotent via the de-dup cache —
    /// notifications already rendered in this process lifetime are skipped.
    /// </summary>
    private async Task RunCatchupAsync(CancellationToken ct)
    {
        // Capture `since` BEFORE the round-trip so concurrent hub deliveries
        // arriving during the GET don't get filtered out of the next catch-up.
        var since = _lastCatchupSince;
        var nextSince = DateTime.UtcNow;

        // First call (since == null) omits the query param so the server
        // drains the full Pending backlog — see FIX-M2B-001 in the field
        // doc on this class. Subsequent calls send the captured timestamp.
        var url = since.HasValue
            ? $"/api/notifications/pending?since={Uri.EscapeDataString(since.Value.ToString("o"))}"
            : "/api/notifications/pending";

        List<PendingNotificationItem>? items;
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                DiagLog.Write($"Catch-up GET {(int)resp.StatusCode}: {body}");
                return;
            }
            items = await resp.Content.ReadFromJsonAsync<List<PendingNotificationItem>>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DiagLog.Write($"Catch-up GET failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (items is null || items.Count == 0)
        {
            DiagLog.Write($"Catch-up: nothing pending since {(since.HasValue ? since.Value.ToString("O") : "(beginning)")}");
            _lastCatchupSince = nextSince;
            return;
        }

        DiagLog.Write($"Catch-up: {items.Count} pending notification(s) since {(since.HasValue ? since.Value.ToString("O") : "(beginning)")}");
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) return;
            await RenderAndReportAsync(item.PayloadJson, item.Signature, source: "catchup");
        }

        _lastCatchupSince = nextSince;
    }

    /// <summary>
    /// Verify HMAC, de-dup, render, and ReportDelivery. Shared between the
    /// hub fanout (OnReceiveNotificationAsync) and the catch-up endpoint
    /// (RunCatchupAsync). De-dup short-circuits BOTH render and
    /// ReportDelivery — once we've delivered a notificationId in this
    /// process, we won't re-acknowledge it through any path.
    /// </summary>
    private async Task RenderAndReportAsync(string payloadJson, string signature, string source)
    {
        if (!HmacVerifier.Verify(payloadJson, signature, _config.SigningKey))
        {
            DiagLog.Write($"{source}: HMAC verification FAILED — payload dropped.");
            return;
        }

        var payload = HmacVerifier.TryDeserialize(payloadJson);
        if (payload is null)
        {
            DiagLog.Write($"{source}: payload deserialization failed after HMAC pass — payload dropped.");
            return;
        }

        // De-dup BEFORE render. Sliding window resets every time we touch the
        // entry, so a notification that gets re-served on every reconnect for
        // an hour stays cached.
        if (_renderedCache.TryGetValue(payload.NotificationId, out _))
        {
            DiagLog.Write($"{source}: notificationId={payload.NotificationId} already rendered — de-dup hit, skipping.");
            return;
        }

        try
        {
            var notification = ToastTemplateBuilder.BuildFromPayload(payload);
            AppNotificationManager.Default.Show(notification);
            DiagLog.Write($"{source}: rendered notificationId={payload.NotificationId}; title='{payload.Title}'");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"{source}: render failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // Mark rendered ONLY after Show() succeeded — a render failure should
        // not poison the cache and prevent a future retry.
        _renderedCache.Set(payload.NotificationId, (byte)1, new MemoryCacheEntryOptions
        {
            SlidingExpiration = DedupWindow,
        });

        try
        {
            await _hub.InvokeAsync("ReportDelivery", payload.NotificationId);
        }
        catch (Exception ex)
        {
            // Hub may be mid-reconnect during catch-up; the next Reconnected
            // catch-up cycle will retry because the delivery is still Pending
            // server-side. The agent dedup cache prevents a re-render but
            // re-acknowledgement is the explicit goal here, so we don't fight
            // it — eventual ReportDelivery wins.
            DiagLog.Write($"{source}: ReportDelivery failed for {payload.NotificationId}: {ex.GetType().Name}: {ex.Message}");
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
