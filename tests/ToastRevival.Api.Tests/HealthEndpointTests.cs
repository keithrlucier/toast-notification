using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Tests for the anonymous <c>GET /api/health</c> liveness endpoint.
/// Validates the response shape uptime probes will rely on so a future
/// payload change can't silently break alerting.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class HealthEndpointTests
{
    private readonly LoadFixture _load;

    public HealthEndpointTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task Health_AnonymousGet_Returns200WithExpectedShape()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);
        var root = doc!.RootElement;

        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));
        Assert.True(root.GetProperty("uptimeSeconds").GetInt32() >= 0);

        var checks = root.GetProperty("checks");

        var db = checks.GetProperty("db");
        Assert.True(db.GetProperty("healthy").GetBoolean());
        Assert.True(db.GetProperty("latencyMs").GetInt32() >= 0);

        var queue = checks.GetProperty("queue");
        Assert.True(queue.GetProperty("healthy").GetBoolean());
        // Channel<Guid>.Reader.Count is supported on Unbounded channels in
        // .NET 8 — depth must be >= 0, not the -1 sentinel.
        Assert.True(queue.GetProperty("depth").GetInt32() >= 0);
    }

    [Fact]
    public async Task Health_DoesNotRequireAuthentication()
    {
        // Belt-and-suspenders: even if a future global authorization policy
        // lands at the middleware level, /api/health stays anonymous.
        // No Authorization header set on the client.
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden,    resp.StatusCode);
    }

    [Fact]
    public async Task Health_CarriesDefensiveHeaders()
    {
        // The defensive-headers middleware runs before authentication, so
        // the health response must carry the same headers every other API
        // response carries. If a future middleware reorder bypasses the
        // defensive layer for anonymous routes, this test catches it before
        // it ships.
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/health");

        Assert.Equal("nosniff",                          SingleHeader(resp, "X-Content-Type-Options"));
        Assert.Equal("DENY",                             SingleHeader(resp, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin",  SingleHeader(resp, "Referrer-Policy"));
        Assert.Contains("camera=()",                     SingleHeader(resp, "Permissions-Policy"));
    }

    private static string SingleHeader(HttpResponseMessage resp, string name)
    {
        if (resp.Headers.TryGetValues(name, out var values))         return string.Join(",", values);
        if (resp.Content.Headers.TryGetValues(name, out var content)) return string.Join(",", content);
        throw new InvalidOperationException($"Response did not carry header '{name}'.");
    }
}
