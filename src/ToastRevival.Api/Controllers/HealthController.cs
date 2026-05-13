using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// Liveness / readiness probe surface for external uptime monitors
/// (UptimeRobot, Pingdom, BetterStack, Datadog, the Lightsail alarm UI,
/// k8s probes if we ever orchestrate, etc.).
///
/// Anonymous on purpose — the entire point is for an unauthenticated
/// probe to verify the API is reachable, the database is reachable, and
/// the background queue isn't wedged. The response body intentionally
/// stays SHALLOW: liveness signals + per-subsystem latency only. No
/// tenant counts, no notification IDs, no env-var values, no stack
/// traces. Anything probe-worthy that's also sensitive belongs on an
/// authenticated admin surface, not here.
///
/// Status mapping:
///   200 OK              — every checked subsystem is healthy.
///   503 ServiceUnavailable — one or more subsystems failed; body lists which.
///
/// External probes should treat 200 as healthy and any other response
/// (including network failure / timeout / 5xx) as unhealthy. The 503
/// distinction lets a probe surface a more specific failure on a
/// dashboard but doesn't change uptime arithmetic.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// DB ping wall-clock budget. Postgres on the private network
    /// typically responds in under 5 ms; 2 seconds is a generous ceiling
    /// that still distinguishes "live" from "wedged."
    /// </summary>
    private static readonly TimeSpan DbPingTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Cached process-start timestamp — captures the boot moment of the
    /// hosting process so uptime is computed without a Stopwatch in DI.
    /// </summary>
    private static readonly DateTime ProcessStartUtc =
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>
    /// Cached assembly version — baked at build time, won't change for
    /// the life of the process.
    /// </summary>
    private static readonly string AssemblyVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    private readonly AppDbContext _db;
    private readonly INotificationQueueService _queue;

    public HealthController(AppDbContext db, INotificationQueueService queue)
    {
        _db = db;
        _queue = queue;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbCheck = await CheckDbAsync(ct);

        // Queue depth is an in-memory channel counter; reading it is free.
        // We capture it but don't fail the health check on a positive value
        // (a queue with items is normal during a burst). External probes
        // that want to alert on stuck-queue scenarios watch the trend over
        // time, not a single sample.
        var queueDepth = _queue.QueueDepth;

        var allHealthy = dbCheck.Healthy;
        var statusText = allHealthy ? "healthy" : "degraded";

        var body = new
        {
            status      = statusText,
            version     = AssemblyVersion,
            uptimeSeconds = (int)(DateTime.UtcNow - ProcessStartUtc).TotalSeconds,
            checks      = new
            {
                db = new
                {
                    healthy   = dbCheck.Healthy,
                    latencyMs = dbCheck.LatencyMs,
                    error     = dbCheck.Error,
                },
                queue = new
                {
                    healthy = true,           // depth is not a failure signal on its own
                    depth   = queueDepth,
                },
            },
        };

        return allHealthy
            ? Ok(body)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }

    private async Task<DbCheckResult> CheckDbAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DbPingTimeout);

            // CanConnectAsync runs a SELECT 1 round-trip and returns false
            // (rather than throwing) when the connection fails. Fast and
            // safe to call on every probe.
            var ok = await _db.Database.CanConnectAsync(cts.Token);
            sw.Stop();
            return new DbCheckResult(
                Healthy:   ok,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                Error:     ok ? null : "CanConnectAsync returned false");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new DbCheckResult(false, (int)sw.ElapsedMilliseconds, "db ping timed out");
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Surface the exception type and short message — never the full
            // stack trace; never inner connection-string details.
            return new DbCheckResult(false, (int)sw.ElapsedMilliseconds, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    private sealed record DbCheckResult(bool Healthy, int LatencyMs, string? Error);
}
