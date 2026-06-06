using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Extensions;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public UsersController(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> List()
    {
        if (!IsAdmin()) return Forbid();

        var users = await _db.Users
            .OrderBy(u => u.Email)
            .Select(u => new UserResponse(
                u.Id,
                u.Email!,
                u.Role.ToString(),
                u.MfaSecret != null,
                u.LastLogin,
                u.CreatedAt))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("invite")]
    public async Task<ActionResult<UserResponse>> Invite([FromBody] InviteUserRequest req)
    {
        if (!IsAdmin()) return Forbid();

        // Privilege-ceiling: a caller may never assign a role above their own.
        // Prevents an Admin from creating a SuperAdmin.
        if (req.Role > GetCallerRole()) return Forbid();

        var tenantId = GetTenantId();

        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == req.Email))
            return Conflict("An account with this email already exists.");

        // AA-M10: Generate a secure random 16-char temporary password internally.
        // The password is returned in the 200 response body for the admin to share;
        // it is NOT logged by middleware. The user must change it on first login.
        var tempPassword = GenerateSecureTempPassword();

        var user = new AppUser
        {
            TenantId = tenantId,
            Email = req.Email.Trim().ToLowerInvariant(),
            UserName = req.Email.Trim().ToLowerInvariant(),
            Role = req.Role,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        // AA-M10: Return temp password in response body (encrypted transport only).
        // Audit event logs "temp password generated" but NOT the value.
        return CreatedAtAction(nameof(List), new { id = user.Id },
            new { userId = user.Id, email = user.Email!, role = user.Role.ToString(),
                  tempPassword, note = "User must change this password on first login." });
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == callerId) return BadRequest("Cannot change your own role.");

        // Privilege-ceiling: a caller may never promote a user above their own
        // role. Prevents an Admin from minting a SuperAdmin.
        if (req.Role > GetCallerRole()) return Forbid();

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.TenantId != GetTenantId()) return NotFound();

        user.Role = req.Role;
        // SES-2-R: rotate the security stamp so the user's existing tokens (which carry
        // the OLD role + old epoch) are revoked on the next request — they must re-login
        // and pick up the new role.
        user.SecurityStamp = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == callerId) return BadRequest("Cannot remove your own account.");

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.TenantId != GetTenantId()) return NotFound();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return StatusCode(500, result.Errors.Select(e => e.Description));

        return NoContent();
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue("tenantId")!);

    private UserRole GetCallerRole() =>
        Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;

    // ARCH-M1: Delegates to the shared ClaimsPrincipalExtensions.IsAdmin().
    private bool IsAdmin() => User.IsAdmin();

    // AA-M10: Generate a cryptographically random 16-character temporary password
    // meeting the AA-M3 password policy (upper, lower, digit, symbol).
    private static string GenerateSecureTempPassword()
    {
        const string upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower   = "abcdefghjkmnpqrstuvwxyz";
        const string digits  = "23456789";
        const string symbols = "!@#$%^&*";
        const string all     = upper + lower + digits + symbols;

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder(16);
        // Ensure at least one character from each required class.
        sb.Append(upper[bytes[0] % upper.Length]);
        sb.Append(lower[bytes[1] % lower.Length]);
        sb.Append(digits[bytes[2] % digits.Length]);
        sb.Append(symbols[bytes[3] % symbols.Length]);
        for (var i = 4; i < 16; i++)
            sb.Append(all[bytes[i] % all.Length]);

        // Shuffle using Fisher-Yates so required chars aren't always at positions 0-3.
        RandomNumberGenerator.Fill(bytes);
        for (var i = 15; i > 0; i--)
        {
            var j = bytes[i] % (i + 1);
            (sb[i], sb[j]) = (sb[j], sb[i]);
        }

        return sb.ToString();
    }
}
