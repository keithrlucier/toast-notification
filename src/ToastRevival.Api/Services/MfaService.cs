using System.Security.Cryptography;
using OtpNet;

namespace ToastRevival.Api.Services;

public class MfaService
{
    private const string Issuer = "Toast Notification";

    /// <summary>
    /// Generates a fresh TOTP secret. Returns the base32-encoded secret string
    /// (store in AppUser.MfaSecret) and the otpauth:// URI for QR code generation.
    /// </summary>
    public (string secret, string qrUri) GenerateEnrollment(string userEmail)
    {
        var secretBytes  = new byte[20];
        RandomNumberGenerator.Fill(secretBytes);
        var base32Secret = Base32Encoding.ToString(secretBytes);

        var qrUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(userEmail)}" +
                    $"?secret={base32Secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

        return (base32Secret, qrUri);
    }

    /// <summary>
    /// Verifies a 6-digit TOTP code against the stored base32 secret.
    /// Allows ±1 time step (30 s window each side) to tolerate clock skew.
    /// Returns false if the secret is null/empty.
    /// </summary>
    public bool Verify(string? storedSecret, string code)
    {
        if (string.IsNullOrWhiteSpace(storedSecret)) return false;
        if (string.IsNullOrWhiteSpace(code)) return false;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(storedSecret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(
                code.Trim(),
                out _,
                new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }
}
