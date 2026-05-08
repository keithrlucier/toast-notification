using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface ITokenService
{
    string CreateUserToken(AppUser user);
    string CreateDeviceToken(Device device);
    string CreateRefreshToken();
}
