using Azure;
using Azure.AI.ContentSafety;
using Microsoft.Extensions.Logging;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

/// <summary>
/// Azure Content Safety–backed moderation service.
///
/// Severity thresholds (D3):
///   0–1  → Pass
///   2–4  → Review  (admin must approve before send)
///   5–6  → Block   (rejected, never queued)
///
/// When ContentSafety:Endpoint or ContentSafety:Key is absent the service
/// degrades gracefully — all content returns Pass. This lets the platform
/// run in staging/dev without an Azure subscription while keeping the code
/// path exercised through the full moderation flow.
/// </summary>
public class ContentSafetyService : IContentModerationService
{
    private readonly ContentSafetyClient? _client;
    private readonly ILogger<ContentSafetyService> _logger;

    public ContentSafetyService(IConfiguration config, ILogger<ContentSafetyService> logger)
    {
        _logger = logger;
        var endpoint = config["ContentSafety:Endpoint"];
        var key      = config["ContentSafety:Key"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
            _client = new ContentSafetyClient(new Uri(endpoint), new AzureKeyCredential(key));
    }

    public async Task<ModerationResult> ModerateTextAsync(
        string title, string? bodyLine1, string? bodyLine2, CancellationToken ct = default)
    {
        if (_client is null) return Pass();

        var text = string.Join(' ', new[] { title, bodyLine1, bodyLine2 }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        try
        {
            var request  = new AnalyzeTextOptions(text);
            var response = await _client.AnalyzeTextAsync(request, ct);
            return Evaluate(response.Value.CategoriesAnalysis.ToDictionary(
                c => c.Category.ToString(),
                c => c.Severity ?? 0),
                imageScores: null);
        }
        catch (Exception ex)
        {
            // Log but degrade to Pass — a transient Azure outage should not block every send
            // TODO: wire to structured logging (ILogger) at M4 when DI logging is configured
            _logger.LogError(ex, "[ContentSafety] text scan failed: {ExType}", ex.GetType().Name);
            return Pass();
        }
    }

    public async Task<ModerationResult> ModerateImageUrlAsync(string imageUrl, CancellationToken ct = default)
    {
        if (_client is null) return Pass();

        try
        {
            var source   = new ContentSafetyImageData(new Uri(imageUrl));
            var request  = new AnalyzeImageOptions(source);
            var response = await _client.AnalyzeImageAsync(request, ct);
            return Evaluate(textScores: null,
                response.Value.CategoriesAnalysis.ToDictionary(
                    c => c.Category.ToString(),
                    c => c.Severity ?? 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ContentSafety] image scan failed: {ExType}", ex.GetType().Name);
            return Pass();
        }
    }

    private static ModerationResult Evaluate(
        Dictionary<string, int>? textScores,
        Dictionary<string, int>? imageScores)
    {
        var allScores = (textScores?.Values ?? (IEnumerable<int>)[]).Concat(imageScores?.Values ?? (IEnumerable<int>)[]);
        var maxScore  = allScores.Any() ? allScores.Max() : 0;

        var decision = maxScore switch
        {
            >= 5 => ModerationDecision.Block,
            >= 2 => ModerationDecision.Review,
            _    => ModerationDecision.Pass,
        };

        return new ModerationResult(decision, textScores, imageScores, BlocklistTerm: null);
    }

    private static ModerationResult Pass() =>
        new(ModerationDecision.Pass, null, null, null);
}
