namespace ToastRevival.Api.Services;

public interface ITurnstileVerifier
{
    bool IsEnabled { get; }
    string? SiteKey { get; }
    Task<TurnstileVerification> VerifyAsync(
        string? token,
        string? remoteIp,
        string expectedAction,
        CancellationToken ct = default);
}

public sealed record TurnstileVerification(
    bool Success,
    string? Hostname = null,
    string? Action = null,
    string? Error = null);
