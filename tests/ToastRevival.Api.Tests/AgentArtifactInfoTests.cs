using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Tests for the anonymous <c>GET /api/agent/intunewin-info</c> endpoint that the
/// admin Install Agent page reads to decide whether to show the Intune download
/// button. Locks the response shape the dashboard depends on — especially the
/// <c>available:false</c> branch, which is what renders the graceful
/// "not yet published" state instead of a dead download link.
/// </summary>
[Collection(nameof(LoadCollection))]
public sealed class AgentArtifactInfoTests
{
    private readonly LoadFixture _load;

    public AgentArtifactInfoTests(LoadFixture load)
    {
        _load = load;
    }

    [Fact]
    public async Task IntuneWinInfo_AnonymousGet_Returns200WithExpectedShape()
    {
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/agent/intunewin-info");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);
        var root = doc!.RootElement;

        // url + version are always present, regardless of whether the file exists.
        Assert.Equal("/downloads/ToastNotification.intunewin", root.GetProperty("url").GetString());
        Assert.True(root.TryGetProperty("version", out _));

        // The test container has no Downloads:RootPath populated, so the .intunewin
        // is absent — this is the env-gated branch the dashboard renders as
        // "Package not yet published". sizeBytes must be 0 and lastModifiedUtc null
        // when the artifact is missing.
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Equal(0, root.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("lastModifiedUtc").ValueKind);
    }

    [Fact]
    public async Task IntuneWinInfo_DoesNotRequireAuthentication()
    {
        // The package is identical for every tenant and carries no secrets, so the
        // metadata endpoint is anonymous (same trust model as /api/agent/version).
        // No Authorization header set on the client.
        await _load.ResetAsync();
        var factory = _load.Factory;

        using var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/agent/intunewin-info");

        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden,    resp.StatusCode);
    }
}
