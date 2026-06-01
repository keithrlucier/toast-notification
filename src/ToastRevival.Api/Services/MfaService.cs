using System.Security.Cryptography;
using OtpNet;
using ToastRevival.Api.Models;

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
    /// Stateless TOTP check against an arbitrary base32 secret, with no replay
    /// bookkeeping. Used to confirm a PENDING enrollment (AuthController.MfaEnrollConfirm)
    /// where the secret isn't yet stored as AppUser.MfaSecret and there is no
    /// LastTotpStep history to guard. Same ±1 step skew window as <see cref="Verify"/>.
    /// </summary>
    public bool VerifySecret(string? base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(base32Secret)) return false;
        if (string.IsNullOrWhiteSpace(code)) return false;

        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
            return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies a 6-digit TOTP code against the user's stored base32 secret
    /// and rejects replay within or before the same 30-second time-step.
    ///
    /// On success: mutates <paramref name="user"/>.<see cref="AppUser.LastTotpStep"/>
    /// to the matched step. The caller is responsible for persisting the
    /// change via <c>SaveChangesAsync</c> — without persistence, replay
    /// rejection only holds for the lifetime of the in-memory entity, not
    /// across requests. AuthController.MfaVerify saves explicitly.
    ///
    /// Returns <c>false</c> when:
    ///   - the user's TOTP secret is null/empty (MFA not enrolled),
    ///   - the code is empty / malformed,
    ///   - OtpNet's <see cref="Totp.VerifyTotp"/> rejects the code (wrong
    ///     digits, outside the ±1 step window),
    ///   - the matched step is &lt;= LastTotpStep (replay).
    ///
    /// Window: ±1 step (30 s each side) to tolerate clock skew. The replay
    /// guard intentionally rejects equality (matched &lt;= last) so a code
    /// accepted in the previous request is unusable in this one even if
    /// it's still in its valid window.
    /// </summary>
    public bool Verify(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.MfaSecret)) return false;
        if (string.IsNullOrWhiteSpace(code)) return false;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(user.MfaSecret);
            var totp = new Totp(secretBytes);
            if (!totp.VerifyTotp(
                    code.Trim(),
                    out var matchedStep,
                    new VerificationWindow(previous: 1, future: 1)))
            {
                return false;
            }

            // Replay rejection. matchedStep is floor(unixSeconds / 30) of the
            // step OtpNet picked. If we have already accepted that step (or
            // an earlier one) for this user, this is a replay or a stale code
            // — reject without mutating LastTotpStep.
            if (user.LastTotpStep.HasValue && matchedStep <= user.LastTotpStep.Value)
                return false;

            user.LastTotpStep = matchedStep;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
