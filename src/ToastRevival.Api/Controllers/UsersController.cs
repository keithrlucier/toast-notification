using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
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

        var user = new AppUser
        {
            TenantId = tenantId,
            Email = req.Email.Trim().ToLowerInvariant(),
            UserName = req.Email.Trim().ToLowerInvariant(),
            Role = req.Role,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return CreatedAtAction(nameof(List), new { id = user.Id },
            new UserResponse(user.Id, user.Email!, user.Role.ToString(), false, null, user.CreatedAt));
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

    private bool IsAdmin() => GetCallerRole() >= UserRole.Admin;
}
