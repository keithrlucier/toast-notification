using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToastRevival.Api.DTOs;

namespace ToastRevival.Api.Services;

/// <summary>
/// AGT-4-R: signs the appearance / lock-screen config the agent fetches, mirroring
/// <see cref="NotificationPayloadBuilder"/>. Closes the asymmetry where the toast path was
/// HMAC-verified end-to-end but the device appearance (overlay + lock screen) rode on TLS
/// alone — a rogue-CA / trusted-proxy MITM could have repointed the fleet's lock screen.
///
/// Same contract as the toast path: pre-serialize on the producer, ship the JSON string +
/// signature, and the agent verifies the HMAC over the EXACT bytes it received before
/// applying — never a reserialization. Key = the per-tenant <c>SigningKey</c> already held
/// by both ends (issued at device registration).
/// </summary>
internal static class AppearanceConfigBuilder
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        WriteIndented = false,
    };

    public static (string SignedPayload, string Signature) BuildSigned(
        OverlayConfigResponse overlay, LockScreenConfigResponse lockScreen, string signingKey)
    {
        // The signed payload is the canonical {overlay, lockScreen} the agent re-parses
        // AFTER verifying — it includes LockScreen.ImageUpdatedAtUtc (DASH-L1) so the
        // cache-bust value is covered by the signature too.
        var payload = new { overlay, lockScreen };
        var signedPayload = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        using var hmac = new HMACSHA256(Convert.FromBase64String(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));
        return (signedPayload, signature);
    }
}
