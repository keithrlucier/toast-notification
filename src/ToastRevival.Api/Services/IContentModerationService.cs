using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public record ModerationResult(
    ModerationDecision Decision,
    Dictionary<string, int>? TextScores,
    Dictionary<string, int>? ImageScores,
    string? BlocklistTerm);

public interface IContentModerationService
{
    /// <summary>
    /// Scans text fields (title, body lines) for content policy violations.
    /// Returns Pass/Review/Block with per-category severity scores (0-6).
    /// </summary>
    Task<ModerationResult> ModerateTextAsync(string title, string? bodyLine1, string? bodyLine2, CancellationToken ct = default);

    /// <summary>
    /// Scans an image URL. Skip approved asset-library assets at the call site.
    /// Returns Pass/Review/Block with image category scores.
    /// </summary>
    Task<ModerationResult> ModerateImageUrlAsync(string imageUrl, CancellationToken ct = default);
}
