using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.AI.ContentSafety;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

/// <summary>
/// Azure Content Safety–backed moderation service (M3 D1/D2/D3, M11 tenant policy).
///
/// Policy is per-tenant (M11). The Tenants row carries:
///   ModerationEnabled / ScanText / ScanImages          — feature toggles
///   ModerationReviewSeverity / ModerationBlockSeverity — thresholds on Azure's 0..6 scale
///   ModerationCustomEndpoint / ModerationCustomKey     — bring-your-own Azure resource
///
/// Credential resolution order per call:
///   1. Tenant's ModerationCustomEndpoint + ModerationCustomKey (BYO)
///   2. Platform default from ContentSafety:Endpoint + ContentSafety:Key config
///   3. None — degrade gracefully to Pass (lets staging/dev run without Azure)
///
/// ContentSafetyClient is cached by (endpoint, key-hash) to avoid the
/// construction cost on every send.
///
/// MOD-1 (anchor, 2026-06-01 — OPEN, owner Keith): every scan path below FAILS OPEN
/// (returns Pass) when the Azure call throws or no client is configured. This is a
/// DELIBERATE availability tradeoff — a transient Azure outage must not block every
/// tenant's sends — and a security-conscious tenant already has a fail-closed knob:
/// ModerationRequireApprovalAll routes all Pass results to human Review
/// (NotificationsController.Send). The open product decision Keith owns: should an
/// Azure *exception* (as opposed to "not configured") degrade to Review instead of
/// Pass for moderation-ENABLED tenants? That trades a silent passthrough for a
/// review-queue flood during an outage. One-line change either way once decided.
/// Anchored so the next sweep does not re-flag the fail-open as an oversight.
/// </summary>
public class ContentSafetyService : IContentModerationService
{
    private static readonly ConcurrentDictionary<string, ContentSafetyClient> _clientCache = new();

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ContentSafetyService> _logger;

    public ContentSafetyService(
        AppDbContext db,
        IConfiguration config,
        IHttpContextAccessor http,
        ILogger<ContentSafetyService> logger)
    {
        _db = db;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public async Task<ModerationResult> ModerateTextAsync(
        string title, string? bodyLine1, string? bodyLine2, CancellationToken ct = default)
    {
        var policy = await GetPolicyAsync(ct);
        if (!policy.Enabled || !policy.ScanText) return Pass();

        var client = ResolveClient(policy);
        if (client is null) return Pass();

        var text = string.Join(' ', new[] { title, bodyLine1, bodyLine2 }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        try
        {
            var request  = new AnalyzeTextOptions(text);
            var response = await client.AnalyzeTextAsync(request, ct);
            return Evaluate(policy,
                response.Value.CategoriesAnalysis.ToDictionary(
                    c => c.Category.ToString(),
                    c => c.Severity ?? 0),
                imageScores: null);
        }
        catch (Exception ex)
        {
            // MOD-1 (anchor): fail-open is deliberate — see class doc. A transient
            // Azure outage should not block every send; tenants needing fail-closed
            // enable ModerationRequireApprovalAll.
            _logger.LogError(ex, "[ContentSafety] text scan failed: {ExType}", ex.GetType().Name);
            return Pass();
        }
    }

    public async Task<ModerationResult> ModerateImageUrlAsync(string imageUrl, CancellationToken ct = default)
    {
        var policy = await GetPolicyAsync(ct);
        if (!policy.Enabled || !policy.ScanImages) return Pass();

        var client = ResolveClient(policy);
        if (client is null) return Pass();

        try
        {
            var source   = new ContentSafetyImageData(new Uri(imageUrl));
            var request  = new AnalyzeImageOptions(source);
            var response = await client.AnalyzeImageAsync(request, ct);
            return Evaluate(policy,
                textScores: null,
                response.Value.CategoriesAnalysis.ToDictionary(
                    c => c.Category.ToString(),
                    c => c.Severity ?? 0));
        }
        catch (Exception ex)
        {
            // MOD-1 (anchor): fail-open is deliberate — see class doc.
            _logger.LogError(ex, "[ContentSafety] image scan failed: {ExType}", ex.GetType().Name);
            return Pass();
        }
    }

    public async Task<ModerationResult> ModerateImageBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var policy = await GetPolicyAsync(ct);
        if (!policy.Enabled || !policy.ScanImages) return Pass();

        var client = ResolveClient(policy);
        if (client is null) return Pass();

        try
        {
            var source   = new ContentSafetyImageData(new BinaryData(bytes));
            var request  = new AnalyzeImageOptions(source);
            var response = await client.AnalyzeImageAsync(request, ct);
            return Evaluate(policy,
                textScores: null,
                response.Value.CategoriesAnalysis.ToDictionary(
                    c => c.Category.ToString(),
                    c => c.Severity ?? 0));
        }
        catch (Exception ex)
        {
            // MOD-1 (anchor): fail-open is deliberate — see class doc.
            _logger.LogError(ex, "[ContentSafety] image bytes scan failed: {ExType}", ex.GetType().Name);
            return Pass();
        }
    }

    /// <summary>
    /// Reads the calling tenant's moderation policy. Falls back to platform defaults
    /// when no JWT/tenant context is available (e.g. unit tests, design-time).
    /// </summary>
    private async Task<ModerationPolicy> GetPolicyAsync(CancellationToken ct)
    {
        var tenantIdClaim = _http.HttpContext?.User?.FindFirstValue("tenantId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            return ModerationPolicy.PlatformDefault(_config);

        // IgnoreQueryFilters because the Tenants filter joins on the calling tenant —
        // this read happens during request handling, so the filter is fine, but we
        // keep it explicit so the read site is unambiguous.
        var t = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(x => x.Id == tenantId)
            .Select(x => new
            {
                x.ModerationEnabled,
                x.ModerationScanText,
                x.ModerationScanImages,
                x.ModerationReviewSeverity,
                x.ModerationBlockSeverity,
                x.ModerationRequireApprovalAll,
                x.ModerationCustomEndpoint,
                x.ModerationCustomKey,
            })
            .FirstOrDefaultAsync(ct);

        if (t is null) return ModerationPolicy.PlatformDefault(_config);

        var endpoint = !string.IsNullOrWhiteSpace(t.ModerationCustomEndpoint) ? t.ModerationCustomEndpoint
                                                                              : _config["ContentSafety:Endpoint"];
        var key      = !string.IsNullOrWhiteSpace(t.ModerationCustomKey)      ? t.ModerationCustomKey
                                                                              : _config["ContentSafety:Key"];

        return new ModerationPolicy(
            Enabled:          t.ModerationEnabled,
            ScanText:         t.ModerationScanText,
            ScanImages:       t.ModerationScanImages,
            ReviewSeverity:   t.ModerationReviewSeverity,
            BlockSeverity:    t.ModerationBlockSeverity,
            Endpoint:         endpoint,
            Key:              key);
    }

    private ContentSafetyClient? ResolveClient(ModerationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Endpoint) || string.IsNullOrWhiteSpace(policy.Key))
            return null;

        var cacheKey = BuildCacheKey(policy.Endpoint, policy.Key);
        return _clientCache.GetOrAdd(cacheKey, _ =>
            new ContentSafetyClient(new Uri(policy.Endpoint), new AzureKeyCredential(policy.Key)));
    }

    private static string BuildCacheKey(string endpoint, string key)
    {
        // Hash the key so we don't hold raw credentials as dictionary keys in memory.
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"{endpoint}|{keyHash}";
    }

    private static ModerationResult Evaluate(
        ModerationPolicy policy,
        Dictionary<string, int>? textScores,
        Dictionary<string, int>? imageScores)
    {
        var allScores = (textScores?.Values ?? (IEnumerable<int>)[]).Concat(imageScores?.Values ?? (IEnumerable<int>)[]);
        var maxScore  = allScores.Any() ? allScores.Max() : 0;

        var decision =
            maxScore >= policy.BlockSeverity  ? ModerationDecision.Block  :
            maxScore >= policy.ReviewSeverity ? ModerationDecision.Review :
                                                ModerationDecision.Pass;

        return new ModerationResult(decision, textScores, imageScores, BlocklistTerm: null);
    }

    private static ModerationResult Pass() =>
        new(ModerationDecision.Pass, null, null, null);

    /// <summary>
    /// Resolved per-call moderation policy — combines tenant overrides with platform defaults.
    /// </summary>
    private record ModerationPolicy(
        bool Enabled,
        bool ScanText,
        bool ScanImages,
        int ReviewSeverity,
        int BlockSeverity,
        string? Endpoint,
        string? Key)
    {
        /// <summary>
        /// Fallback policy when no tenant context is available — read straight from
        /// platform config. Matches the pre-M11 hard-coded behavior.
        /// </summary>
        public static ModerationPolicy PlatformDefault(IConfiguration config) => new(
            Enabled:        true,
            ScanText:       true,
            ScanImages:     true,
            ReviewSeverity: 2,
            BlockSeverity:  5,
            Endpoint:       config["ContentSafety:Endpoint"],
            Key:            config["ContentSafety:Key"]);
    }
}
