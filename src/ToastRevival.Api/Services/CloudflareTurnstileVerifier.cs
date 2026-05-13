using System.Text.Json.Serialization;

namespace ToastRevival.Api.Services;

public sealed class CloudflareTurnstileVerifier : ITurnstileVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _http;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CloudflareTurnstileVerifier> _logger;
    private readonly string? _secretKey;
    private readonly bool _required;

    public CloudflareTurnstileVerifier(
        HttpClient http,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<CloudflareTurnstileVerifier> logger)
    {
        _http = http;
        _env = env;
        _logger = logger;
        SiteKey = Clean(config["Turnstile:SiteKey"]);
        _secretKey = Clean(config["Turnstile:SecretKey"]);
        _required = config.GetValue<bool?>("Turnstile:Required") ?? !_env.IsDevelopment();
    }

    public string? SiteKey { get; }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(_secretKey);

    public async Task<TurnstileVerification> VerifyAsync(
        string? token,
        string? remoteIp,
        string expectedAction,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            if (_required)
                return new TurnstileVerification(false, Error: "Human verification is not configured.");

            return new TurnstileVerification(true);
        }

        if (string.IsNullOrWhiteSpace(token))
            return new TurnstileVerification(false, Error: "Complete the human verification challenge.");

        if (token.Length > 2048)
            return new TurnstileVerification(false, Error: "Human verification token is invalid.");

        var request = new TurnstileRequest(
            _secretKey!,
            token.Trim(),
            string.IsNullOrWhiteSpace(remoteIp) ? null : remoteIp,
            Guid.NewGuid().ToString());

        try
        {
            using var response = await _http.PostAsJsonAsync(SiteVerifyUrl, request, ct);
            var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: ct);
            if (!response.IsSuccessStatusCode || result is null)
                return new TurnstileVerification(false, Error: "Human verification failed. Please try again.");

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Turnstile rejected trial request from {RemoteIp}: {ErrorCodes}",
                    remoteIp,
                    string.Join(",", result.ErrorCodes ?? Array.Empty<string>()));
                return new TurnstileVerification(false, result.Hostname, result.Action, "Human verification failed. Please try again.");
            }

            if (!string.Equals(result.Action, expectedAction, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Turnstile action mismatch. Expected {ExpectedAction}, got {Action}",
                    expectedAction,
                    result.Action);
                return new TurnstileVerification(false, result.Hostname, result.Action, "Human verification failed. Please reload and try again.");
            }

            return new TurnstileVerification(true, result.Hostname, result.Action);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Turnstile validation failed due to provider or network error.");
            return new TurnstileVerification(false, Error: "Human verification is temporarily unavailable. Please try again.");
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)) return null;
        return value.Trim();
    }

    private sealed record TurnstileRequest(
        [property: JsonPropertyName("secret")] string Secret,
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("remoteip")] string? RemoteIp,
        [property: JsonPropertyName("idempotency_key")] string IdempotencyKey);

    private sealed record TurnstileResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
