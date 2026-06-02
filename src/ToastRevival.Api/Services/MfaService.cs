using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ToastRevival.Api.Data;
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
    /// Pure TOTP match against a base32 secret with the standard ±1 step skew
    /// window. No replay bookkeeping, no mutation, no DB. Returns the matched
    /// RFC 6238 time-step in <paramref name="matchedStep"/> on success so a
    /// caller can enforce the replay floor however it persists state.
    /// </summary>
    private static bool TryMatch(string? base32Secret, string code, out long matchedStep)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(base32Secret)) return false;
        if (string.IsNullOrWhiteSpace(code)) return false;

        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
            return totp.VerifyTotp(code.Trim(), out matchedStep, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stateless TOTP check against an arbitrary base32 secret, with no replay
    /// bookkeeping. Used to confirm a PENDING enrollment (AuthController.MfaEnrollConfirm)
    /// where the secret isn't yet stored as AppUser.MfaSecret and there is no
    /// LastTotpStep history to guard. Same ±1 step skew window as <see cref="Verify"/>.
    /// </summary>
    public bool VerifySecret(string? base32Secret, string code) =>
        TryMatch(base32Secret, code, out _);

    /// <summary>
    /// In-memory TOTP verify with replay-floor check. Mutates
    /// <paramref name="user"/>.<see cref="AppUser.LastTotpStep"/> on success but
    /// does NOT persist anything.
    ///
    /// NOTE: this overload is NOT safe to use as the production replay guard.
    /// Because the check-and-set is in memory and the caller persists separately,
    /// two concurrent requests loaded from independent DbContexts can both pass
    /// (AUTH-H1). Production login / step-up paths MUST use
    /// <see cref="VerifyAndClaimAsync"/>, which performs the floor check and the
    /// advance as one atomic SQL UPDATE. This method is retained as the pure
    /// in-memory core exercised by MfaServiceTests.
    ///
    /// Returns <c>false</c> when the secret is null/empty, the code is empty /
    /// malformed, OtpNet rejects it (wrong digits / outside the ±1 step window),
    /// or the matched step is &lt;= LastTotpStep (replay).
    /// </summary>
    public bool Verify(AppUser user, string code)
    {
        if (!TryMatch(user.MfaSecret, code, out var matchedStep)) return false;

        // Replay rejection. If we have already accepted that step (or an earlier
        // one) for this user, this is a replay or a stale code.
        if (user.LastTotpStep.HasValue && matchedStep <= user.LastTotpStep.Value)
            return false;

        user.LastTotpStep = matchedStep;
        return true;
    }

    /// <summary>
    /// AUTH-H1 — atomic, replay-safe TOTP verification. Verifies the code against
    /// <paramref name="user"/>.MfaSecret, then advances LastTotpStep to the matched
    /// step using a single conditional UPDATE
    /// (<c>WHERE last_totp_step IS NULL OR last_totp_step &lt; @matchedStep</c>).
    /// The check and the state change are one SQL statement, so two concurrent
    /// requests presenting the same intercepted code race the UPDATE and exactly
    /// one observes rows-affected == 1 — the other is rejected. This mirrors the
    /// XT-1 single-use enrollment-token claim in DevicesController.
    ///
    /// Returns <c>true</c> only when the code is valid AND this request won the
    /// atomic advance. Returns <c>false</c> for an invalid/expired/replayed code
    /// or when another concurrent request already advanced the floor.
    /// </summary>
    public async Task<bool> VerifyAndClaimAsync(AppDbContext db, AppUser user, string code)
    {
        if (!TryMatch(user.MfaSecret, code, out var matchedStep)) return false;

        var claimed = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id
                && (u.LastTotpStep == null || u.LastTotpStep < matchedStep))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpStep, matchedStep));

        if (claimed != 1) return false;

        // Keep the tracked entity coherent with what the atomic UPDATE wrote, so a
        // subsequent SaveChangesAsync on the same context is a no-op for this column.
        user.LastTotpStep = matchedStep;
        return true;
    }
}
