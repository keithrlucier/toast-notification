using ToastRevival.Agent.Core;
using Xunit;

namespace ToastRevival.Agent.Tests;

// CR-P1-005 item 3 — agent startup simulation. Pure-logic unit tests for the three
// startup/lifecycle decision points (connect backoff, self-update trigger, config resolution)
// extracted into ToastRevival.Agent.Core. No SignalR server, registry, or filesystem needed.

public class BackoffDelayTests
{
    [Theory]
    [InlineData(0, 5)]    // first retry
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]   // 80 capped to 60
    [InlineData(5, 60)]   // steady state
    [InlineData(12, 60)]
    public void ComputeDelay_FollowsCappedExponentialSchedule(int attempt, int expectedSeconds)
        => Assert.Equal(expectedSeconds, AgentBackoff.ComputeDelay(attempt).TotalSeconds);

    [Fact]
    public void ComputeDelay_NeverExceeds60Seconds()
    {
        for (var attempt = 0; attempt < 25; attempt++)
            Assert.True(AgentBackoff.ComputeDelay(attempt).TotalSeconds <= 60);
    }

    [Fact]
    public void ComputeDelay_IsMonotonicNonDecreasing()
    {
        var prev = AgentBackoff.ComputeDelay(0);
        for (var attempt = 1; attempt < 10; attempt++)
        {
            var next = AgentBackoff.ComputeDelay(attempt);
            Assert.True(next >= prev);
            prev = next;
        }
    }
}

public class SelfUpdateVersionDecisionTests
{
    [Theory]
    [InlineData("0.4.44", "0.4.43", true)]   // newer -> update
    [InlineData("1.0.0", "0.4.43", true)]
    [InlineData("0.4.43", "0.4.43", false)]  // equal -> no update
    [InlineData("0.4.42", "0.4.43", false)]  // older -> no update
    [InlineData("not-a-version", "0.4.43", false)]
    [InlineData(null, "0.4.43", false)]
    [InlineData("", "0.4.43", false)]
    public void TryGetNewerServerVersion_DecidesCorrectly(string? server, string running, bool expectNewer)
    {
        var isNewer = UpdateDecision.TryGetNewerServerVersion(server, Version.Parse(running), out var parsed);
        Assert.Equal(expectNewer, isNewer);
        if (expectNewer) Assert.NotNull(parsed);
    }

    [Fact]
    public void TryGetNewerServerVersion_ParsesServerVersionForLogging()
    {
        var ok = UpdateDecision.TryGetNewerServerVersion("2.1.0", new Version(1, 0, 0), out var parsed);
        Assert.True(ok);
        Assert.Equal(new Version(2, 1, 0), parsed);
    }
}

public class BootstrapEnvParseTests
{
    [Fact]
    public void TryParse_ValidTenantAndServer_ReturnsValues()
    {
        var tenantId = Guid.NewGuid();
        var result = BootstrapEnv.TryParse(tenantId.ToString(), "https://toastnotification.com");
        Assert.NotNull(result);
        Assert.Equal(tenantId, result!.Value.TenantId);
        Assert.Equal("https://toastnotification.com", result.Value.ServerUrl);
    }

    [Theory]
    [InlineData(null, "https://x")]                                   // no tenant
    [InlineData("not-a-guid", "https://x")]                            // bad tenant
    [InlineData("11111111-1111-1111-1111-111111111111", null)]         // no server
    [InlineData("11111111-1111-1111-1111-111111111111", "")]           // empty server
    [InlineData("11111111-1111-1111-1111-111111111111", "   ")]        // whitespace server
    public void TryParse_Invalid_ReturnsNull(string? tenant, string? server)
        => Assert.Null(BootstrapEnv.TryParse(tenant, server));
}
