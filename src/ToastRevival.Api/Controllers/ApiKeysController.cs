using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// Per-tenant API key management (D5). Admin+ only.
/// Keys are intended for programmatic access to the notification API from RMM tools.
/// The full key is returned exactly once at creation; only the SHA-256 hash is stored.
///
/// ANCHOR API-1 (owner: Keith — PRODUCT decision: implement-or-remove).
/// These keys are currently INERT: nothing in the request pipeline ever reads
/// TenantApiKey.KeyHash to authenticate a request (the only AddAuthentication scheme is
/// JwtBearer) and LastUsedAt is never written — so the dashboard advertises "programmatic
/// access" that does not actually work. There is no live security hole today (no key-auth
/// path = no bypass), which is why this is Info, not a vuln. But it must be resolved one
/// of two ways: (a) IMPLEMENT it properly — a custom ApiKey auth handler with a
/// constant-time hash compare, explicit tenant binding, scope limits, and an explicit
/// decision that key-auth must NOT bypass the new MFA gates (Tenant.RequireMfa); or
/// (b) REMOVE the feature + its dashboard UI so nothing is advertised as working. Held
/// for Keith's call; the dashboard copy was left unchanged so as not to pre-empt that
/// decision. Anchored so the next sweep does not re-flag the inert keys as a missed bug.
/// </summary>
[ApiController]
[Route("api/apikeys")]
[Authorize]
public class ApiKeysController : ControllerBase
{
    private readonly AppDbContext _db;

    public ApiKeysController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiKeyResponse>>> List()
    {
        if (!IsAdminOrAbove()) return Forbid();

        var keys = await _db.TenantApiKeys
            .OrderBy(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse(
                k.Id,
                k.Name,
                k.KeyPrefix,
                k.CreatedAt,
                k.LastUsedAt,
                k.RevokedAt != null))
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Create([FromBody] CreateApiKeyRequest req)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);

        // Generate 32 random bytes → base64url string
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var fullKey = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var keyPrefix = fullKey[..8];
        var keyHash   = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullKey)))
                               .ToLowerInvariant();

        var entity = new TenantApiKey
        {
            TenantId  = tenantId,
            Name      = req.Name,
            KeyPrefix = keyPrefix,
            KeyHash   = keyHash,
        };

        _db.TenantApiKeys.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new ApiKeyCreatedResponse(
            entity.Id,
            entity.Name,
            entity.KeyPrefix,
            fullKey,
            entity.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (!IsAdminOrAbove()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var key = await _db.TenantApiKeys.FindAsync(id);
        if (key is null) return NotFound();
        if (key.TenantId != tenantId) return NotFound();
        if (key.RevokedAt is not null) return Conflict("Key is already revoked.");

        key.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private bool IsAdminOrAbove()
    {
        var role = User.FindFirstValue("role");
        return role is nameof(UserRole.Admin) or nameof(UserRole.SuperAdmin);
    }
}
