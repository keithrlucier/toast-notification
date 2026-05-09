using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;

    public AuthController(UserManager<AppUser> userManager, AppDbContext db, ITokenService tokens)
    {
        _userManager = userManager;
        _db = db;
        _tokens = tokens;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest req)
    {
        if (await _db.Tenants.AnyAsync(t => t.Subdomain == req.Subdomain))
            return Conflict("Subdomain already taken.");

        // Wrap in transaction — orphaned Tenant row if user creation fails otherwise
        using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Name = req.TenantName,
            Subdomain = req.Subdomain,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var user = new AppUser
        {
            TenantId = tenant.Id,
            Email = req.Email,
            UserName = req.Email,
            Role = UserRole.Admin,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync();
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        await tx.CommitAsync();

        var token = _tokens.CreateUserToken(user);
        var refresh = _tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return Ok(new AuthResponse(token, refresh, expiresAt, user.Id, tenant.Id, user.Role.ToString()));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest req)
    {
        // Bypass tenant filter — login is tenant-unaware
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user is null || !await _userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized("Invalid credentials.");

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _tokens.CreateUserToken(user);
        var refresh = _tokens.CreateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return Ok(new AuthResponse(token, refresh, expiresAt, user.Id, user.TenantId, user.Role.ToString()));
    }
}
