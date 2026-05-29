using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Windows.AppNotifications;

namespace ToastRevival.Agent;

internal enum AgentConnectionState
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    Error,
}

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
            tenantId      = bootstrap.TenantId,
            deviceName    = Environment.MachineName,
            username      = Environment.UserName,
            osVersion     = Environment.OSVersion.VersionString,
            agentVersion  = ThisAssembly.Version,
            enrollmentKey = bootstrap.EnrollmentKey,
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
                dto.DeviceId, dto.Token, dto.SigningKey, dto.TenantName);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"Registration exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private record DeviceTokenResponse(string Token, Guid DeviceId, Guid TenantId, string SigningKey, string? TenantName = null);

    /// <summary>
    /// Fetches the current tenant display name and logo URL from the server.
    /// Called on every agent startup so the notification attribution (top of
    /// every toast) reflects the latest name + icon even if the tenant was
    /// renamed or re-branded after this device registered.
    ///
    /// Returns (null, null) on any error (network unavailable, token expired,
    /// etc.) so callers can fall back gracefully to the name stored in
    /// config.json and the bundled icon.
    /// </summary>
    public static async Task<TenantRefreshResult> TryRefreshTenantInfoAsync(DeviceConfig config, CancellationToken ct)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerUrl),
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", config.DeviceToken) },
        };

        try
        {
            using var resp = await http.GetAsync("/api/devices/tenant-name", ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"TryRefreshTenantInfoAsync: server returned {(int)resp.StatusCode}");
                return new TenantRefreshResult(null, null);
            }

            var dto = await resp.Content.ReadFromJsonAsync<TenantAttributionDto>(cancellationToken: ct);
            var name    = string.IsNullOrWhiteSpace(dto?.TenantName) ? null : dto!.TenantName;
            var logoUrl = string.IsNullOrWhiteSpace(dto?.LogoUrl)    ? null : dto!.LogoUrl;
            if (logoUrl is not null
                && Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var serverUri)
                && Uri.TryCreate(serverUri, logoUrl, out var resolvedLogoUri))
            {
                logoUrl = resolvedLogoUri.ToString();
            }
            return new TenantRefreshResult(name, logoUrl);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"TryRefreshTenantInfoAsync: {ex.GetType().Name}: {ex.Message}");
            return new TenantRefreshResult(null, null);
        }
    }

    /// <summary>Wire shape of GET /api/devices/tenant-name. LogoUrl is optional
    /// so 0.4.5 servers (which don't return it) deserialize cleanly.</summary>
    private record TenantAttributionDto(string TenantName, string? LogoUrl = null);

    /// <summary>
    /// M12 — fetches the bundled device-appearance config (desktop overlay +
    /// lock screen) for this device's tenant. Called at startup right after the
    /// tenant-name refresh. Device-JWT authenticated. Returns null on any error
    /// (network, non-200, expired token, or a pre-M12 server that 404s the
    /// route) so the caller leaves whatever appearance was last applied.
    /// </summary>
    public static async Task<AppearanceConfig?> TryGetAppearanceConfigAsync(DeviceConfig config, CancellationToken ct)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.ServerUrl),
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", config.DeviceToken) },
        };

        try
        {
            using var resp = await http.GetAsync("/api/devices/appearance-config", ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"TryGetAppearanceConfigAsync: server returned {(int)resp.StatusCode}");
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<AppearanceConfig>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"TryGetAppearanceConfigAsync: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Snapshot of the tenant identity returned by the server. Name nullable so a
/// failed refresh doesn't overwrite the cached name in config.json; logoUrl
/// nullable so the agent falls back to the bundled icon when the tenant
/// hasn't uploaded a logo.
/// </summary>
internal sealed record TenantRefreshResult(string? Name, string? LogoUrl);

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
    /// Sliding-expiration window for the notificationId de-dup cache. SignalR
    /// can redeliver buffered ReceiveNotification messages after a reconnect,
    /// and the catch-up endpoint can serve a notification the hub already
    /// pushed in the same connection — both paths share this cache so a
    /// notification is rendered + acknowledged at most once per hour-long
    /// sliding window.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(1);

    public event EventHandler<AgentConnectionState>? ConnectionStateChanged;
    public event Action? OnDecommissioned;
    public event Action? OnUninstallRequested;

    private readonly DeviceConfig _config;
    private readonly HubConnection _hub;
    private readonly HttpClient _http;
    // Bounded dedup cache. 50k entries × ~100 bytes ≈ 5MB ceiling.
    private readonly MemoryCache _renderedCache = new(new MemoryCacheOptions { SizeLimit = 50_000 });
    // Bounded activation dedup cache. Both ToastNotification.Activated (legacy path) and
    // AppNotificationManager.NotificationInvoked (WinAppSDK path) can fire for the SAME physical
    // click on some OS builds (observed on Win11), so HandleActivationAsync dedups on the exact
    // argument string within a short window to ReportInteraction / open the URL exactly once.
    private readonly MemoryCache _activationCache = new(new MemoryCacheOptions { SizeLimit = 1_024 });
    private static readonly TimeSpan ActivationDedupWindow = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _pingLoop;

    /// <summary>
    /// Lower bound for the next catch-up call. Null on first run so the
    /// initial GET drains the full backlog — the most common scenario is an
    /// agent that rebooted and reconnects with Pending deliveries that
    /// pre-date this process. Initializing to UtcNow at ctor time would have
    /// the first call exclude every pre-existing Pending delivery
    /// (CreatedAt &lt; ctor_time). After each successful catch-up, set to
    /// UtcNow so subsequent calls only fetch deliveries created since the
    /// last drain.
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
            // Reconnect intervals: 0, 2, 5, 10, 30 seconds. After 30s the
            // SignalR client keeps trying at the last interval.
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
        _hub.On("DeviceDecommissioned", () =>
        {
            DiagLog.Write("DeviceDecommissioned: clearing config for immediate re-registration.");
            try { File.Delete(ConfigStore.GetConfigPath()); } catch { /* best-effort */ }
            OnDecommissioned?.Invoke();
            _shutdown.Cancel();
        });
        _hub.On("UninstallAgent", () =>
        {
            DiagLog.Write("UninstallAgent: remote uninstall command received.");
            // Fire-and-forget: restore lock screen, write trigger file, fire SYSTEM task.
            // Cancel shutdown so PrimaryMode exits its wait loop while the async work runs.
            _ = Task.Run(async () =>
            {
                try { await SelfUpdateService.RequestUninstallAsync(CancellationToken.None); }
                catch (Exception ex)
                {
                    DiagLog.Write($"UninstallAgent: RequestUninstallAsync failed: {ex.GetType().Name}: {ex.Message}");
                }
            });
            OnUninstallRequested?.Invoke();
            _shutdown.Cancel();
        });
        _hub.Reconnecting += ex =>
        {
            DiagLog.Write($"Hub reconnecting: {ex?.GetType().Name}: {ex?.Message}");
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        _hub.Reconnected += async id =>
        {
            DiagLog.Write($"Hub reconnected: connectionId={id}");
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Connected);
            // Drain anything sent while we were disconnected. Fire-and-forget
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
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Disconnected);
            return Task.CompletedTask;
        };

        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        DiagLog.Write($"Hub starting: deviceId={_config.DeviceId}");
        ConnectionStateChanged?.Invoke(this, AgentConnectionState.Connecting);
        await _hub.StartAsync(ct);
        DiagLog.Write($"Hub started: state={_hub.State}; connectionId={_hub.ConnectionId}");
        ConnectionStateChanged?.Invoke(this, AgentConnectionState.Connected);

        // Report version on every connect so the dashboard reflects it after an MSI
        // upgrade (which reuses existing config.json and never re-registers).
        _ = ReportVersionAsync(ct);

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

    /// <summary>
    /// Manual reconnect triggered from the tray icon context menu. Stops the hub
    /// cleanly (fires Closed → Disconnected), re-enters Reconnecting state, restarts,
    /// and runs a catch-up pass on success.
    /// </summary>
    public async Task ReconnectAsync()
    {
        try
        {
            DiagLog.Write("ReconnectAsync: manual reconnect initiated.");
            await _hub.StopAsync(_shutdown.Token);
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Reconnecting);
            await _hub.StartAsync(_shutdown.Token);
            DiagLog.Write("ReconnectAsync: hub restarted.");
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Connected);
            try { await RunCatchupAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (Exception ex) { DiagLog.Write($"ReconnectAsync catch-up: {ex.GetType().Name}: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ReconnectAsync failed: {ex.GetType().Name}: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, AgentConnectionState.Disconnected);
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
        _activationCache.Dispose();
        _shutdown.Dispose();
    }

    private async Task OnReceiveNotificationAsync(string payloadJson, string signature)
    {
        await RenderAndReportAsync(payloadJson, signature, source: "hub");
    }

    /// <summary>
    /// Max page size requested per /pending call. Server clamps this to
    /// [1, 500]; we request the ceiling so a long-offline drain finishes in
    /// fewer round-trips, each round-trip costing one slot of the
    /// device-catchup-per-hour=60/hr rate-limit budget. With CatchupPageSize=500
    /// the per-hour drain ceiling is 30,000 notifications.
    /// </summary>
    private const int CatchupPageSize = 500;

    /// <summary>
    /// Fetch every Pending delivery for this device since the last known
    /// good window and run them through the same verify+render+report
    /// pipeline as live hub messages. Idempotent via the de-dup cache —
    /// notifications already rendered in this process lifetime are skipped.
    ///
    /// Pages until a partial page is returned. Each loop iteration advances
    /// `since` to the last item's CreatedAt + 1 tick so the next GET excludes
    /// the rows we just processed. A partial page (Count &lt; CatchupPageSize)
    /// means the server returned everything that matched — exit the loop.
    /// </summary>
    private async Task RunCatchupAsync(CancellationToken ct)
    {
        // Capture `since` BEFORE the round-trip so concurrent hub deliveries
        // arriving during the GET don't get filtered out of the next catch-up.
        var since = _lastCatchupSince;
        var nextSince = DateTime.UtcNow;
        var totalDrained = 0;

        // Hard ceiling on iteration count — defense in depth against a server
        // bug returning the same rows over and over. With CatchupPageSize=500
        // and the per-hour rate limit of 60, the server-bounded ceiling is
        // already 60. This is just so we never spin.
        const int MaxLoops = 64;

        for (var loop = 0; loop < MaxLoops; loop++)
        {
            if (ct.IsCancellationRequested) return;

            // First call (since == null) omits the `since` query param so the
            // server drains the full Pending backlog. Always include limit so
            // newer servers honor our preferred page size.
            var url = since.HasValue
                ? $"/api/notifications/pending?since={Uri.EscapeDataString(since.Value.ToString("o"))}&limit={CatchupPageSize}"
                : $"/api/notifications/pending?limit={CatchupPageSize}";

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
                if (loop == 0)
                    DiagLog.Write($"Catch-up: nothing pending since {(since.HasValue ? since.Value.ToString("O") : "(beginning)")}");
                else
                    DiagLog.Write($"Catch-up: drained {totalDrained} notification(s) across {loop} page(s).");
                _lastCatchupSince = nextSince;
                return;
            }

            totalDrained += items.Count;
            DiagLog.Write($"Catch-up page {loop + 1}: {items.Count} item(s) since {(since.HasValue ? since.Value.ToString("O") : "(beginning)")}");

            foreach (var item in items)
            {
                if (ct.IsCancellationRequested) return;
                await RenderAndReportAsync(item.PayloadJson, item.Signature, source: "catchup");
            }

            // Partial page → server returned everything that matched, drain done.
            if (items.Count < CatchupPageSize)
            {
                DiagLog.Write($"Catch-up: drained {totalDrained} notification(s) across {loop + 1} page(s) (final page partial).");
                _lastCatchupSince = nextSince;
                return;
            }

            // Full page → advance `since` past the last item's CreatedAt and
            // loop. +1 tick prevents re-fetching the boundary row (server
            // filter is `CreatedAt >= since`).
            since = items[^1].CreatedAt.AddTicks(1);
        }

        DiagLog.Write($"Catch-up: hit MaxLoops={MaxLoops} guard after draining {totalDrained}; advancing _lastCatchupSince anyway.");
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
            // Pass the in-process activation callback: legacy-WinRT toasts deliver
            // clicks through ToastNotification.Activated, NOT through
            // AppNotificationManager.NotificationInvoked (see LegacyToastShim).
            LegacyToastShim.Show(notification, OnLegacyToastActivated);
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
            Size = 1,
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

    // Live click path for legacy-WinRT toasts (LegacyToastShim.ToastNotification.Activated).
    // This is the one that actually fires for our toasts; OnNotificationInvoked below is
    // kept for the WinAppSDK Show() path but does not fire for legacy-dispatched toasts.
    private void OnLegacyToastActivated(string argument) => _ = HandleActivationAsync(argument);

    private async void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        => await HandleActivationAsync(args.Argument);

    /// <summary>
    /// Routes a toast click. <paramref name="argument"/> is a key=value;key=value string —
    /// the same shape AppNotificationBuilder.AddArgument produced into the toast XML, surfaced
    /// either by ToastNotification.Activated (legacy path) or AppNotificationActivatedEventArgs
    /// (WinAppSDK path). Reports the interaction to the hub and opens the action URL if present.
    /// </summary>
    private async Task HandleActivationAsync(string argument)
    {
        // Collapse duplicate events for one physical click (legacy Activated + WinAppSDK
        // NotificationInvoked can both fire). The argument string is identical across both,
        // and hub toasts include notificationId so distinct toasts never collide.
        if (!string.IsNullOrEmpty(argument))
        {
            if (_activationCache.TryGetValue(argument, out _))
            {
                DiagLog.Write($"Toast activation de-dup: argument='{argument}' already handled, skipping.");
                return;
            }
            _activationCache.Set(argument, (byte)1, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ActivationDedupWindow,
                Size = 1,
            });
        }

        var parsed = ParseToastArguments(argument);
        DiagLog.Write($"Toast activated: argument='{argument}'");

        if (parsed.TryGetValue("source", out var source) && source == "hub"
            && parsed.TryGetValue("notificationId", out var idStr)
            && Guid.TryParse(idStr, out var notificationId))
        {
            var action = parsed.GetValueOrDefault("action") ?? "click";
            var url = parsed.GetValueOrDefault("url");
            try
            {
                await _hub.InvokeAsync("ReportInteraction", notificationId, action);
                DiagLog.Write($"ReportInteraction: notificationId={notificationId}; action={action}");
            }
            catch (Exception ex)
            {
                DiagLog.Write($"ReportInteraction failed for {notificationId}: {ex.GetType().Name}: {ex.Message}");
            }

            ToastUrlLauncher.OpenIfAllowed(url);
        }
    }

    private async Task ReportVersionAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/devices/ping", new { agentVersion = ThisAssembly.Version }, ct);
            DiagLog.Write($"ReportVersion: {ThisAssembly.Version} -> {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ReportVersion failed: {ex.GetType().Name}: {ex.Message}");
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
                using var resp = await _http.PostAsJsonAsync("/api/devices/ping", new { agentVersion = ThisAssembly.Version }, ct);
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

internal static class ToastUrlLauncher
{
    public static bool OpenIfAllowed(string? encodedUrl)
    {
        if (string.IsNullOrWhiteSpace(encodedUrl)) return false;

        Uri uri;
        try
        {
            var url = Uri.UnescapeDataString(encodedUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || parsed is null
                || parsed.Scheme is not ("http" or "https"))
            {
                DiagLog.Write("ToastUrlLauncher: rejected non-http(s) URL.");
                return false;
            }
            uri = parsed;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ToastUrlLauncher: rejected malformed URL: {ex.GetType().Name}.");
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            DiagLog.Write($"ToastUrlLauncher: opened URL for host '{uri.Host}'.");
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ToastUrlLauncher: open failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}

internal static class ThisAssembly
{
    public static string Version =>
        typeof(ThisAssembly).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
