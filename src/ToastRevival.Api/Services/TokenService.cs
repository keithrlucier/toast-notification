using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public string CreateUserToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim("tenantId", user.TenantId.ToString()),
            new Claim("role", user.Role.ToString()),
            new Claim("type", "user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (user.IsPlatformAdmin)
            claims.Add(new Claim("platformAdmin", "true"));

        return BuildToken(claims.ToArray(), GetExpiresAt("Jwt:ExpiresInMinutes", 60, isMinutes: true));
    }

    public string CreateDeviceToken(Device device)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, device.Id.ToString()),
            new Claim("tenantId", device.TenantId.ToString()),
            new Claim("deviceId", device.Id.ToString()),
            new Claim("type", "device"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        return BuildToken(claims, GetExpiresAt("Jwt:DeviceTokenExpiresInDays", 365, isMinutes: false));
    }

    public string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public string CreateMfaToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim("tenantId", user.TenantId.ToString()),
            new Claim("role", user.Role.ToString()),
            new Claim("type", "user"),
            new Claim("mfa", "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (user.IsPlatformAdmin)
            claims.Add(new Claim("platformAdmin", "true"));

        return BuildToken(claims.ToArray(), DateTime.UtcNow.AddMinutes(15));
    }

    private string BuildToken(Claim[] claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private DateTime GetExpiresAt(string configKey, int defaultValue, bool isMinutes)
    {
        var value = int.TryParse(_config[configKey], out var v) ? v : defaultValue;
        return isMinutes ? DateTime.UtcNow.AddMinutes(value) : DateTime.UtcNow.AddDays(value);
    }
}
