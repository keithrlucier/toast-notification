using System.Security.Claims;

namespace ToastRevival.Api.Utilities;

public static class MfaElevationExtensions
{
    /// <summary>
    /// True when the principal carries a FRESH step-up elevation: the <c>mfa=true</c>
    /// claim is present AND the elevation (<c>mfa_at</c> claim, unix seconds) happened
    /// within <c>Jwt:MfaElevationExpiresInMinutes</c> (default 15).
    ///
    /// Freshness is tracked via <c>mfa_at</c>, NOT token lifetime — the MFA-elevated
    /// token doubles as the session token (it carries the full session lifetime so the
    /// frontend can use it as the active Bearer). Tying freshness to the token's <c>exp</c>
    /// would expire the whole session 15 minutes after any sensitive action and log the
    /// user out. Separating the two keeps the session alive while still forcing
    /// re-verification of sensitive actions (send a toast, change the lock screen,
    /// broadcast-to-all) on the short window.
    ///
    /// A token minted before <c>mfa_at</c> existed (or any non-elevated token) returns
    /// false — the caller simply re-verifies once. Fail-closed.
    /// </summary>
    public static bool HasFreshMfa(this ClaimsPrincipal user, IConfiguration config)
    {
        if (user.FindFirstValue("mfa") != "true") return false;
        if (!long.TryParse(user.FindFirstValue("mfa_at"), out var elevatedAtUnix)) return false;

        var minutes = int.TryParse(config["Jwt:MfaElevationExpiresInMinutes"], out var m) ? m : 15;
        var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - elevatedAtUnix;
        return ageSeconds >= 0 && ageSeconds <= minutes * 60L;
    }
}
