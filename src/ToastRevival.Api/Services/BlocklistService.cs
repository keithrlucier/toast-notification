using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

/// <summary>
/// Checks notification content against the tenant's custom banned-term list.
/// Called before Azure Content Safety — a blocklist hit short-circuits and
/// returns Block without paying for an external scan.
/// </summary>
public class BlocklistService
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

        var combined = string.Concat(
            title, ' ', bodyLine1 ?? "", ' ', bodyLine2 ?? "")
            .ToLowerInvariant();

        foreach (var term in terms)
        {
            if (combined.Contains(term.ToLowerInvariant()))
                return new ModerationResult(ModerationDecision.Block, null, null, BlocklistTerm: term);
        }

        return null;
    }
}
