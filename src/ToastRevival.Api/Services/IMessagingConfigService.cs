namespace ToastRevival.Api.Services;

public interface IMessagingConfigService
{
    MessagingConfigSnapshot GetSnapshot();
    Task<MessagingConfigSnapshot> UpdateAsync(
        string? clickSendUsername,
        string? clickSendApiKey,
        string? mailjetApiKey,
        string? mailjetApiSecret,
        string? mailjetSenderEmail,
        CancellationToken cancellationToken = default);
}

public sealed record MessagingConfigSnapshot(
    bool    HasClickSendUsername,
    bool    HasClickSendApiKey,
    bool    HasMailjetApiKey,
    bool    HasMailjetApiSecret,
    bool    HasMailjetSenderEmail,
    string? MaskedClickSendUsername,
    string? MaskedClickSendApiKey,
    string? MaskedMailjetApiKey,
    string? MaskedMailjetApiSecret,
    string? MailjetSenderEmail);
