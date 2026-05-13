namespace ToastRevival.Api.Services;

public interface ISmsService
{
    Task SendAsync(string toPhone, string message);
}
