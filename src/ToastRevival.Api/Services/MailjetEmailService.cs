using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ToastRevival.Api.Services;

public class MailjetEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly string _senderEmail;
    private readonly string _senderName;

    public MailjetEmailService(IConfiguration config, HttpClient http)
    {
        _http = http;
        var apiKey    = config["Mailjet:ApiKey"]    ?? throw new InvalidOperationException("Mailjet:ApiKey is required.");
        var secretKey = config["Mailjet:SecretKey"] ?? throw new InvalidOperationException("Mailjet:SecretKey is required.");
        _senderEmail  = config["Mailjet:SenderEmail"] ?? "support@toastnotification.com";
        _senderName   = config["Mailjet:SenderName"]  ?? "Toast Notification";

        _http.BaseAddress = new Uri("https://api.mailjet.com/");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{secretKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var payload = new
        {
            Messages = new[]
            {
                new
                {
                    From = new { Email = _senderEmail, Name = _senderName },
                    To   = new[] { new { Email = toEmail, Name = toName } },
                    Subject  = subject,
                    HTMLPart = htmlBody,
                }
            }
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await _http.PostAsync("v3.1/send", content);
        resp.EnsureSuccessStatusCode();
    }
}
