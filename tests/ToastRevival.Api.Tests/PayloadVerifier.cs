using System.Security.Cryptography;
using System.Text;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Mirrors the agent-side HMAC verification path. Kept in the test project
/// because the production agent lives in a Windows-only project that this
/// netstandard test assembly cannot reference. The verification logic is
/// trivial enough to reproduce — the byte-deterministic property is enforced
/// by the production-side <c>NotificationPayloadBuilder.BuildSigned</c>.
/// </summary>
internal static class PayloadVerifier
{
    public static bool Verify(string payloadJson, string signatureBase64, string signingKeyBase64)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(signingKeyBase64));
        var actual   = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        var expected = Convert.FromBase64String(signatureBase64);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
