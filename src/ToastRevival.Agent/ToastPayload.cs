using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToastRevival.Agent;

/// <summary>
/// Wire shape pushed by the backend over SignalR (NotificationQueueService.BuildSignedPayload).
/// Deserialization MUST be byte-compatible with the server's serialization or HMAC
/// verification will fail. We never reserialize — the server-sent JSON string is what we sign over.
/// </summary>
internal sealed class ToastPayload
{
    [JsonPropertyName("notificationId")] public Guid NotificationId { get; set; }
    [JsonPropertyName("title")]          public string Title { get; set; } = "";
    [JsonPropertyName("bodyLine1")]      public string? BodyLine1 { get; set; }
    [JsonPropertyName("bodyLine2")]      public string? BodyLine2 { get; set; }
    [JsonPropertyName("heroImageUrl")]   public string? HeroImageUrl { get; set; }
    [JsonPropertyName("logoUrl")]        public string? LogoUrl { get; set; }
    [JsonPropertyName("actionButtons")]  public List<PayloadButton>? ActionButtons { get; set; }
    [JsonPropertyName("audioSetting")]   public string? AudioSetting { get; set; }
    [JsonPropertyName("scenario")]       public string? Scenario { get; set; }
}

internal sealed class PayloadButton
{
    [JsonPropertyName("label")]     public string Label { get; set; } = "";
    [JsonPropertyName("action")]    public string Action { get; set; } = "";
    [JsonPropertyName("isPrimary")] public bool IsPrimary { get; set; }
}

internal static class HmacVerifier
{
    /// <summary>
    /// Constant-time HMAC-SHA256 verification. Returns false on any failure
    /// (key parse error, base64 decode error, length mismatch, content mismatch)
    /// without leaking which failure mode happened — the caller should reject the
    /// payload either way.
    /// </summary>
    public static bool Verify(string payloadJson, string signatureBase64, string signingKeyBase64)
    {
        try
        {
            var key = Convert.FromBase64String(signingKeyBase64);
            var expected = Convert.FromBase64String(signatureBase64);

            using var hmac = new HMACSHA256(key);
            var actual = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static ToastPayload? TryDeserialize(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ToastPayload>(payloadJson);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ToastPayload deserialize failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
