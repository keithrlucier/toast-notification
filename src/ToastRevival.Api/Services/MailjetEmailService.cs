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
        // Services-H1: the admin panel (MessagingConfigService / SystemController) and
        // .env.example persist the secret under "Mailjet:ApiSecret" — read that first so
        // UI-configured email actually works (the consumer previously read the never-written
        // "Mailjet:SecretKey"). Fall back to the legacy key so any deployment that set
        // Mailjet__SecretKey out-of-band keeps working through the rename.
        var secretKey = config["Mailjet:ApiSecret"] ?? config["Mailjet:SecretKey"]
                        ?? throw new InvalidOperationException("Mailjet:ApiSecret is required.");
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
