using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface ITokenService
{
    string CreateUserToken(AppUser user);
    string CreateDeviceToken(Device device);
    string CreateRefreshToken();
    /// <summary>
    /// Short-lived MFA-elevated token (15 min). Contains all user claims plus
    /// mfa=true. Required for broadcast-to-all and other Super Admin actions.
    /// </summary>
    string CreateMfaToken(AppUser user);
}
