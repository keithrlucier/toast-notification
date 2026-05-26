using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// Tenant-configurable banned term list (D7). Admin+ only. Terms are matched
/// case-insensitively against notification title and body fields before sending.
/// A blocklist hit returns ModerationDecision.Block — the notification is
/// rejected and never queued.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class BlocklistController : ControllerBase
{
    private readonly AppDbContext _db;

    public BlocklistController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BlocklistEntryResponse>>> List()
    {
        if (!IsAdminOrAbove()) return Forbid();

        var entries = await _db.TenantBlocklistEntries
            .OrderBy(b => b.Term)
            .Select(b => new BlocklistEntryResponse(b.Id, b.Term, b.CreatedAt))
            .ToListAsync();

        return Ok(entries);
    }

    [HttpPost]
    public async Task<ActionResult<BlocklistEntryResponse>> Add([FromBody] AddBlocklistEntryRequest req)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var userId   = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var term     = req.Term.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(term) || term.Length > 500)
            return BadRequest("Term must be 1–500 characters.");

        // Duplicate guard (unique index will catch this too, but friendlier error)
        if (await _db.TenantBlocklistEntries.AnyAsync(b => b.Term == term))
            return Conflict("Term already in blocklist.");

        var entry = new TenantBlocklistEntry
        {
            TenantId = tenantId,
            Term = term,
            CreatedByUserId = userId,
        };
        _db.TenantBlocklistEntries.Add(entry);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new BlocklistEntryResponse(entry.Id, entry.Term, entry.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var entry = await _db.TenantBlocklistEntries.FindAsync(id);
        if (entry is null) return NotFound();
        if (entry.TenantId != tenantId) return NotFound();

        _db.TenantBlocklistEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private bool IsAdminOrAbove()
    {
        var role = User.FindFirstValue("role");
        return role is nameof(UserRole.Admin) or nameof(UserRole.SuperAdmin);
    }
}
