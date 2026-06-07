namespace ToastRevival.Agent.Core;

/// <summary>CR-P0-004 initial hub-connect backoff: 5s, 10s, 20s, 40s, 60s, then 60s steady.</summary>
public static class AgentBackoff
{
    /// <param name="attempt">0-based retry attempt.</param>
    public static TimeSpan ComputeDelay(int attempt)
        => TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt)));
}

/// <summary>Self-update trigger decision: update only when the server version parses and is strictly newer.</summary>
public static class UpdateDecision
{
    public static bool TryGetNewerServerVersion(string? serverVersion, Version running, out Version? serverVer)
    {
        serverVer = Version.TryParse(serverVersion, out var v) ? v : null;
        return serverVer is not null && serverVer > running;
    }
}

/// <summary>Env-var bootstrap parse (the dev/diagnostic override tier of config resolution).</summary>
public static class BootstrapEnv
{
    /// <summary>Returns (tenantId, serverUrl) only when the tenant id is a valid GUID and the
    /// server url is non-blank; otherwise null.</summary>
    public static (Guid TenantId, string ServerUrl)? TryParse(string? tenantId, string? serverUrl)
        => Guid.TryParse(tenantId, out var tid) && !string.IsNullOrWhiteSpace(serverUrl)
            ? (tid, serverUrl)
            : null;
}
