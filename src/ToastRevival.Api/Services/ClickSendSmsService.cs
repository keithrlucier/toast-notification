using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ToastRevival.Api.Services;

public class ClickSendSmsService : ISmsService
{
    private readonly HttpClient _http;

    public ClickSendSmsService(IConfiguration config, HttpClient http)
    {
        _http = http;
        var username = config["ClickSend:Username"] ?? throw new InvalidOperationException("ClickSend:Username is required.");
        var apiKey   = config["ClickSend:ApiKey"]   ?? throw new InvalidOperationException("ClickSend:ApiKey is required.");

        _http.BaseAddress = new Uri("https://rest.clicksend.com/");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiKey}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public async Task SendAsync(string toPhone, string message)
    {
        var e164 = NormalizeE164(toPhone);

        var payload = new
        {
            messages = new[]
            {
                new
                {
                    source = "sdk",
                    from   = "ToastNotif",
                    body   = message,
                    to     = e164,
                }
            }
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await _http.PostAsync("v3/sms/send", content);
        resp.EnsureSuccessStatusCode();
    }

    // Strip formatting and ensure E.164. Defaults to +1 if no country code present.
    private static string NormalizeE164(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) digits = "1" + digits;          // US/CA bare 10-digit
        return "+" + digits;
    }
}
