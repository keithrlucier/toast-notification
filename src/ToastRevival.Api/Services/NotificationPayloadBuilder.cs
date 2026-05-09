using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

/// <summary>
/// Single source of truth for the wire shape and signature of a notification
/// payload. The hub fanout (NotificationQueueService) and the catch-up endpoint
/// (NotificationsController.GetPending) MUST sign the same byte sequence — an
/// agent that reconnected mid-fanout could receive the same notification once
/// from the hub and once from catch-up; both copies must HMAC-verify against
/// the same key without the agent caring which path delivered it.
///
/// Standing rule (M2.A #14): pre-serialize on the producer, ship the JSON
/// string + signature as separate transport args, never trust transport-side
/// reserialization.
/// </summary>
internal static class NotificationPayloadBuilder
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        WriteIndented = false,
    };

    public static (string PayloadJson, string Signature) BuildSigned(Notification n, string signingKey)
    {
        var payload = new
        {
            notificationId = n.Id,
            title = n.Title,
            bodyLine1 = n.BodyLine1,
            bodyLine2 = n.BodyLine2,
            heroImageUrl = n.HeroImageUrl,
            logoUrl = n.LogoUrl,
            actionButtons = n.ActionButtonsJson is not null
                ? JsonSerializer.Deserialize<JsonElement?>(n.ActionButtonsJson)
                : null,
            audioSetting = n.AudioSetting,
            scenario = n.Scenario.ToString().ToLower(),
        };

        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        using var hmac = new HMACSHA256(Convert.FromBase64String(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson)));
        return (payloadJson, signature);
    }
}
