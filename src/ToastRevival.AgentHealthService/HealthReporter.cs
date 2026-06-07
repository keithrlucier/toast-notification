using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ToastRevival.Agent.Core;

namespace ToastRevival.AgentHealthService;

/// <summary>
/// The whole service. On a fixed cadence: read the machine-level bootstrap from
/// HKLM, then POST the machine name to /api/agent/health/{tenantId}. The endpoint
/// stamps LastPing on the device row(s) that already exist for this machine, so the
/// dashboard shows the device online whenever the machine is powered on — no
/// interactive user required, no per-user device token (which this LocalSystem
/// process could not decrypt anyway).
/// </summary>
internal sealed class HealthReporter : BackgroundService
{
    // 15-minute cadence: comfortably inside the dashboard's 45-minute "online"
    // window with margin for two missed beats, and ~2.5x lighter than the agent's
    // 6-minute heartbeat. This is a coarse liveness signal, not telemetry.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    // Same HKLM key the agent's DeviceConfig.TryLoadBootstrapFromRegistry reads.
    private const string BootstrapRegistryKey = @"SOFTWARE\Toast2IT\Toast Notification";

    private readonly ILogger<HealthReporter> _log;
    private readonly HttpClient _http = new();

    public HealthReporter(ILogger<HealthReporter> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("ToastNotificationHealth started; cadence {Minutes} min.", Interval.TotalMinutes);

        // The whole loop is wrapped so a graceful stop (cancellation) exits cleanly
        // and any unexpected throw in the loop is logged rather than silently killing
        // the service — for a liveness service, staying alive IS the feature.
        try
        {
            // Fire once promptly so a freshly-booted machine shows online without
            // waiting a full interval, then settle into the cadence.
            await PingOnceAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PingOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service stopping — normal.
        }
    }

    private async Task PingOnceAsync(CancellationToken ct)
    {
        try
        {
            // Read config EVERY tick (not once at startup): an auto-start service can
            // come up before the MSI has written HKLM on a brand-new install — reading
            // each tick lets it self-heal on the next beat without a restart.
            var config = ReadConfig();
            if (config is null)
            {
                _log.LogWarning("No HKLM bootstrap yet (TenantId/ServerUrl); skipping this tick.");
                return;
            }

            var payload = new HealthPingPayload(Environment.MachineName, config.EnrollmentKey);
            var url = new Uri(new Uri(config.ServerUrl), $"/api/agent/health/{config.TenantId}");

            using var resp = await _http.PostAsJsonAsync(
                url, payload, HealthJsonContext.Default.HealthPingPayload, ct).ConfigureAwait(false);
            _log.LogInformation("Health ping -> {Status} ({Url})", (int)resp.StatusCode, url);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service stopping — normal.
        }
        catch (Exception ex)
        {
            // Best-effort: a failed ping (network not up, DNS, proxy, server down)
            // must never crash the service. Next tick retries.
            _log.LogWarning(ex, "Health ping failed: {Type}: {Message}", ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Reads TenantId + ServerUrl (+ optional legacy EnrollmentKey) from HKLM. The
    /// GUID/non-blank-URL validation is delegated to the unit-tested
    /// <see cref="BootstrapEnv.TryParse"/> so the rules stay identical to the agent.
    /// </summary>
    private HealthConfig? ReadConfig()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BootstrapRegistryKey);
            if (key is null) return null;

            var parsed = BootstrapEnv.TryParse(
                key.GetValue("TenantId") as string,
                key.GetValue("ServerUrl") as string);
            if (parsed is null) return null;

            var enroll = key.GetValue("EnrollmentKey") as string;
            if (string.IsNullOrWhiteSpace(enroll)) enroll = null;

            return new HealthConfig(parsed.Value.TenantId, parsed.Value.ServerUrl, enroll);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading HKLM bootstrap failed: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return null;
        }
    }
}
