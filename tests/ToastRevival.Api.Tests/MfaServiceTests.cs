using OtpNet;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Unit tests for <see cref="MfaService"/>. Pure-function (with mutation
/// of the passed-in <see cref="AppUser"/>) — no DB or fixture wiring.
///
/// Closes SEC-005 (INFO-M3-001): TOTP code replay within the ±1 step
/// verification window. <see cref="MfaService.Verify"/> now records the
/// matched step on the user and rejects any subsequent code whose matched
/// step is &lt;= the recorded value. Caller (AuthController.MfaVerify)
/// persists via <c>SaveChangesAsync</c> so the floor survives across
/// requests.
/// </summary>
public sealed class MfaServiceTests
{
    private readonly MfaService _mfa = new();

    [Fact]
    public void Verify_AcceptsFreshCode_RecordsMatchedStep()
    {
        var user = NewEnrolledUser(out var code);

        Assert.True(_mfa.Verify(user, code));
        Assert.NotNull(user.LastTotpStep);
    }

    [Fact]
    public void Verify_RejectsSameCodeOnSecondCall_BlocksReplayWithinStep()
    {
        // SEC-005 / INFO-M3-001: an attacker who intercepts a valid TOTP
        // submission within its 30-second window must not be able to replay
        // it before the legitimate user does. The step-floor guard rejects
        // the second submission because its matched step is no greater than
        // the first call's recorded step.
        var user = NewEnrolledUser(out var code);

        Assert.True(_mfa.Verify(user, code));
        var firstStep = user.LastTotpStep;
        Assert.NotNull(firstStep);

        Assert.False(_mfa.Verify(user, code));
        Assert.Equal(firstStep, user.LastTotpStep);
    }

    [Fact]
    public void Verify_RejectsCodeOlderThanRecordedStep()
    {
        // Synthetic: pretend the user already accepted a code in step T.
        // A code whose matched step is < T (clock skew, intentional rewind)
        // must be rejected without bumping the floor.
        var user = NewEnrolledUser(out var code);

        Assert.True(_mfa.Verify(user, code));
        var floor = user.LastTotpStep!.Value;

        // Bump the floor 5 steps into the future — any code minted at the
        // current real time has matchedStep <= floor + 1 < floor + 5,
        // tripping the replay guard.
        user.LastTotpStep = floor + 5;

        var freshCode = MintCode(user.MfaSecret!);
        Assert.False(_mfa.Verify(user, freshCode));
        Assert.Equal(floor + 5, user.LastTotpStep);
    }

    [Fact]
    public void Verify_ReturnsFalseWhenSecretMissing()
    {
        var user = new AppUser { MfaSecret = null };
        Assert.False(_mfa.Verify(user, "123456"));
        Assert.Null(user.LastTotpStep);
    }

    [Fact]
    public void Verify_ReturnsFalseOnInvalidCode()
    {
        var user = NewEnrolledUser(out _);
        Assert.False(_mfa.Verify(user, "000000"));
    }

    [Fact]
    public void Verify_ReturnsFalseOnEmptyCode()
    {
        var user = NewEnrolledUser(out _);
        Assert.False(_mfa.Verify(user, ""));
        Assert.False(_mfa.Verify(user, "   "));
    }

    private AppUser NewEnrolledUser(out string code)
    {
        var (secret, _) = _mfa.GenerateEnrollment("totp.test@pen.test");
        code = MintCode(secret);
        return new AppUser
        {
            Email     = "totp.test@pen.test",
            MfaSecret = secret,
        };
    }

    private static string MintCode(string base32Secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
        return totp.ComputeTotp();
    }
}
