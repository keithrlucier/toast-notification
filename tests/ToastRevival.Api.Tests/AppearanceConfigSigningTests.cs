using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Services;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// AGT-4-R unit tests for <see cref="AppearanceConfigBuilder"/>. Pure function — no DB.
/// Locks the HMAC round-trip contract the Windows agent depends on (HmacVerifier.Verify
/// uses the identical HMAC-SHA256 + FixedTimeEquals math), and the case-insensitive
/// deserialize that the agent MUST use — the QA pass nearly shipped a case-sensitive
/// variant that would have silently dropped every appearance config.
/// </summary>
public sealed class AppearanceConfigSigningTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static (OverlayConfigResponse Overlay, LockScreenConfigResponse LockScreen) Sample() => (
        new OverlayConfigResponse(true, new[] { "hostname", "user" }, "bottom-right", "Property of Acme", 85),
        new LockScreenConfigResponse(true, "https://toastnotification.com/assets/lockscreen/abc.png",
            new DateTime(2026, 6, 2, 1, 44, 43, DateTimeKind.Utc)));

    private static string Hmac(string payload, string keyB64)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(keyB64));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    [Fact]
    public void BuildSigned_SignatureRecomputesWithSameKey()
    {
        var key = NewKey();
        var (overlay, lockScreen) = Sample();
        var (payload, signature) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, key);
        Assert.Equal(Hmac(payload, key), signature); // what the agent recomputes must match
    }

    [Fact]
    public void BuildSigned_TamperedPayloadDoesNotMatchSignature()
    {
        var key = NewKey();
        var (overlay, lockScreen) = Sample();
        var (payload, signature) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, key);
        var tampered = payload.Replace("bottom-right", "top-left"); // attacker repoints the overlay
        Assert.NotEqual(signature, Hmac(tampered, key));
    }

    [Fact]
    public void BuildSigned_WrongKeyDoesNotVerify()
    {
        var (overlay, lockScreen) = Sample();
        var (payload, signature) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, NewKey());
        Assert.NotEqual(signature, Hmac(payload, NewKey())); // a different tenant's key can't verify
    }

    [Fact]
    public void SignedPayload_DeserializesCaseInsensitively_WithAllFields()
    {
        var (overlay, lockScreen) = Sample();
        var (payload, _) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, NewKey());

        var probe = JsonSerializer.Deserialize<Probe>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(probe);
        Assert.NotNull(probe!.Overlay);
        Assert.NotNull(probe.LockScreen);
        Assert.True(probe.Overlay!.Enabled);
        Assert.Equal("bottom-right", probe.Overlay.Position);
        Assert.Equal(85, probe.Overlay.OpacityPercent);
        Assert.Equal("https://toastnotification.com/assets/lockscreen/abc.png", probe.LockScreen!.ImageUrl);
        Assert.NotNull(probe.LockScreen.ImageUpdatedAtUtc); // DASH-L1 cache-bust is inside the signature
    }

    [Fact]
    public void SignedPayload_CaseSensitiveDeserialize_BindsOuterKeysToNull()
    {
        // The signed payload's outer keys are the server's anonymous-object names
        // ("overlay"/"lockScreen", lowercase). A case-SENSITIVE deserialize binds them to
        // null — proving WHY the agent uses JsonSerializerDefaults.Web (the rejected QA
        // "use case-sensitive" suggestion would have dropped every config).
        var (overlay, lockScreen) = Sample();
        var (payload, _) = AppearanceConfigBuilder.BuildSigned(overlay, lockScreen, NewKey());
        var probe = JsonSerializer.Deserialize<Probe>(payload); // default options = case-sensitive
        Assert.Null(probe!.Overlay);
        Assert.Null(probe.LockScreen);
    }

    private sealed record Probe(OverlayConfigResponse? Overlay, LockScreenConfigResponse? LockScreen);
}
