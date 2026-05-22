using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class AssetsController : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly AppDbContext _db;
    private readonly IContentModerationService _moderation;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;

    public AssetsController(
        AppDbContext db,
        IContentModerationService moderation,
        IAuditService audit,
        IWebHostEnvironment env)
    {
        _db = db;
        _moderation = moderation;
        _audit = audit;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AssetResponse>>> List()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        var assets = await _db.AssetLibrary
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new AssetResponse(
                a.Id, a.Name, a.Type.ToString(), a.Url,
                a.ModerationResultJson, a.UploadedAt))
            .ToListAsync();

        return Ok(assets);
    }

    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024 + 4096)] // file limit + form overhead
    public async Task<ActionResult<AssetResponse>> Upload(
        IFormFile file,
        [FromForm] string? name,
        [FromForm] string? assetType)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "File is required." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "File exceeds the 5 MB size limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "Only image files are allowed (.jpg, .jpeg, .png, .gif, .webp)." });

        if (!Enum.TryParse<AssetType>(assetType ?? "HeroImage", true, out var type))
            type = AssetType.HeroImage;

        // Read bytes — bounded by RequestSizeLimit above
        await using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

        // Moderation scan before persisting — no chicken-and-egg with URL scanning
        var modResult = await _moderation.ModerateImageBytesAsync(bytes);

        if (modResult.Decision == ModerationDecision.Block)
            return UnprocessableEntity(new
            {
                error = "content_blocked",
                message = "Image blocked by content moderation policy.",
            });

        // Persist file to wwwroot/assets/{tenantId}/{assetId}{ext}
        // Path traversal is not possible — assetId is a Guid, ext is validated above
        var assetId    = Guid.NewGuid();
        var webRoot    = _env.WebRootPath;
        var uploadDir  = Path.Combine(webRoot, "assets", tenantId.ToString());
        Directory.CreateDirectory(uploadDir);

        var fileName = $"{assetId}{ext}";
        var filePath = Path.Combine(uploadDir, fileName);
        await System.IO.File.WriteAllBytesAsync(filePath, bytes);

        // URL is absolute so the Windows agent can fetch it from a toast payload
        var url = $"{Request.Scheme}://{Request.Host}/assets/{tenantId}/{fileName}";

        var moderationJson = JsonSerializer.Serialize(new
        {
            decision    = modResult.Decision.ToString(),
            imageScores = modResult.ImageScores,
        });

        var asset = new AssetLibrary
        {
            Id                  = assetId,
            TenantId            = tenantId,
            Name                = (name?.Trim().Length > 0 ? name.Trim() : null)
                                  ?? Path.GetFileNameWithoutExtension(file.FileName),
            Type                = type,
            Url                 = url,
            ContentHash         = hash,
            ModerationResultJson = moderationJson,
            UploadedBy          = userId,
        };

        _db.AssetLibrary.Add(asset);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, userId, "asset.upload", "AssetLibrary",
            assetId.ToString(),
            new { asset.Name, asset.Type, moderation = modResult.Decision.ToString() },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return CreatedAtAction(nameof(List), new AssetResponse(
            asset.Id, asset.Name, asset.Type.ToString(),
            asset.Url, asset.ModerationResultJson, asset.UploadedAt));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<AssetResponse>> Rename(Guid id, [FromBody] RenameAssetRequest body)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var name = body?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { message = "Name is required." });
        if (name.Length > 200)
            return BadRequest(new { message = "Name must be 200 characters or fewer." });

        var asset = await _db.AssetLibrary.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);
        if (asset is null) return NotFound();

        var oldName = asset.Name;
        asset.Name = name;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, userId, "asset.rename", "AssetLibrary",
            id.ToString(), new { oldName, newName = name },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new AssetResponse(
            asset.Id, asset.Name, asset.Type.ToString(),
            asset.Url, asset.ModerationResultJson, asset.UploadedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var asset = await _db.AssetLibrary.FindAsync(id);
        if (asset is null || asset.TenantId != tenantId) return NotFound();

        // Delete file from disk — reconstruct path from tenantId + assetId (not from raw URL)
        // to prevent path traversal from a corrupted DB value
        var webRoot   = _env.WebRootPath;
        var uri       = new Uri(asset.Url);
        var segments  = uri.AbsolutePath.Split('/');
        var fileName  = segments.LastOrDefault();
        if (!string.IsNullOrEmpty(fileName))
        {
            var resolved = Path.GetFullPath(Path.Combine(webRoot, "assets", tenantId.ToString(), fileName));
            var allowed  = Path.GetFullPath(Path.Combine(webRoot, "assets", tenantId.ToString()));
            if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(resolved))
            {
                System.IO.File.Delete(resolved);
            }
        }

        _db.AssetLibrary.Remove(asset);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(tenantId, userId, "asset.delete", "AssetLibrary",
            id.ToString(), new { asset.Name },
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }
}

public record AssetResponse(
    Guid Id,
    string Name,
    string Type,
    string Url,
    string? ModerationResultJson,
    DateTime UploadedAt);

public record RenameAssetRequest(string? Name);
