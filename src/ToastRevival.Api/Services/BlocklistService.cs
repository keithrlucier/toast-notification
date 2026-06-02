using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

/// <summary>
/// Checks notification content against the tenant's custom banned-term list.
/// Called before Azure Content Safety — a blocklist hit short-circuits and
/// returns Block without paying for an external scan.
/// </summary>
public class BlocklistService : IBlocklistService
{
    private readonly AppDbContext _db;

    public BlocklistService(AppDbContext db) => _db = db;

    /// <summary>
    /// Returns a ModerationResult with Decision=Block and the matched term,
    /// or null if no blocklist term matched.
    /// </summary>
    public async Task<ModerationResult?> CheckAsync(
        string title, string? bodyLine1, string? bodyLine2, CancellationToken ct = default)
    {
        var terms = await _db.TenantBlocklistEntries
            .Select(b => b.Term)
            .ToListAsync(ct);

        if (terms.Count == 0) return null;

        var combined = NormalizeForMatch(string.Concat(
            title, ' ', bodyLine1 ?? "", ' ', bodyLine2 ?? ""));

        foreach (var term in terms)
        {
            if (combined.Contains(NormalizeForMatch(term)))
                return new ModerationResult(ModerationDecision.Block, null, null, BlocklistTerm: term);
        }

        return null;
    }

    /// <summary>
    /// BLK-1 (2026-06-01): fold away common substring-match evasions before comparing.
    /// NFKC compatibility normalization collapses full-width / ligature / styled
    /// look-alikes onto their plain forms, and Unicode "format" code points
    /// (zero-width space/joiner U+200B–200D, BOM U+FEFF, bidi marks, soft hyphen)
    /// are stripped so "b​adword" can't slip a banned term past a raw Contains().
    /// Case-folded last. NOTE: this deliberately does NOT fold cross-script homoglyphs
    /// (e.g. Cyrillic 'а' vs Latin 'a') — that needs a confusables map and is a larger
    /// change; Azure Content Safety remains the severity gate behind this
    /// tenant-custom-term blocklist.
    /// </summary>
    private static string NormalizeForMatch(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
                continue;
            sb.Append(ch);
        }
        return sb.ToString().ToLowerInvariant();
    }
}
