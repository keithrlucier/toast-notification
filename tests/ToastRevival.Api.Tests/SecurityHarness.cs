using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Helpers for the security pen-test surface. Two responsibilities:
///
/// 1. <see cref="SeedTenantAsync"/> — seed a tenant with one admin user and
///    N devices via DI scope. Distinct from <see cref="LoadHarness"/> in two
///    ways: the seeded admin's role is configurable (so privilege-escalation
///    tests can mint Technician + Admin + SuperAdmin tokens), and the
///    returned record exposes the admin <see cref="AppUser"/> + the tenant
///    <see cref="Tenant"/> rows themselves so individual tests can mutate
///    state (e.g. set <c>EnrollmentKey</c>) without re-querying.
///
/// 2. <see cref="ForgeUserJwt"/> / <see cref="ForgeDeviceJwt"/> — mint JWTs
///    with arbitrary claim sets and signing keys. The production
///    <see cref="TokenService"/> reads <c>IsPlatformAdmin</c> from the DB
///    row, so it cannot mint a forged platformAdmin claim for a non-admin.
///    The pen-tests need that ability to verify the JwtBearer middleware
///    rejects forgeries signed with the wrong key, and to construct expired
///    or claim-tampered tokens for negative paths.
/// </summary>
internal static class SecurityHarness
{
    public sealed record SeededPenTenant(
        Guid TenantId,
        Tenant Tenant,
        string SigningKey,
        AppUser AdminUser,
        string AdminToken,
        IReadOnlyList<SeededPenDevice> Devices);

    public sealed record SeededPenDevice(Guid DeviceId, string Token);

    public static async Task<SeededPenTenant> SeedTenantAsync(
        ApiTestFactory factory,
        UserRole role             = UserRole.SuperAdmin,
        int deviceCount           = 0,
        bool isPlatformAdmin      = false,
        string tenantNamePrefix   = "PenTest")
    {
        if (deviceCount < 0) throw new ArgumentOutOfRangeException(nameof(deviceCount));

        using var scope = factory.Services.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager  = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var slug = Guid.NewGuid().ToString("n");
        var tenant = new Tenant
        {
            Name          = $"{tenantNamePrefix} {slug}",
            Subdomain     = $"pen-{slug[..16]}",
            SigningKey    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            BillingStatus = BillingStatus.Trialing,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var adminEmail = $"admin-{Guid.NewGuid():n}@pen.test";
        var admin = new AppUser
        {
            UserName        = adminEmail,
            Email           = adminEmail,
            EmailConfirmed  = true,
            TenantId        = tenant.Id,
            Role            = role,
            IsPlatformAdmin = isPlatformAdmin,
        };
        var createResult = await userManager.CreateAsync(admin, "PenTestPass!2026");
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to seed pen-test admin: {errors}");
        }

        var adminToken = tokenService.CreateUserToken(admin);

        var devices = new List<Device>(deviceCount);
        for (int i = 0; i < deviceCount; i++)
        {
            devices.Add(new Device
            {
                TenantId          = tenant.Id,
                DeviceName        = $"pen-dev-{i:D5}",
                Username          = $"pen-user-{i:D5}",
                OsVersion         = "Windows 11 26100",
                AgentVersion      = "0.4.0.0",
                RegistrationToken = $"pen-harness-not-jwt-path-{Guid.NewGuid():n}",
                Status            = DeviceStatus.Active,
            });
        }
        if (devices.Count > 0)
        {
            db.Devices.AddRange(devices);
            await db.SaveChangesAsync();
        }

        var seededDevices = devices
            .Select(d => new SeededPenDevice(d.Id, tokenService.CreateDeviceToken(d)))
            .ToList();

        return new SeededPenTenant(tenant.Id, tenant, tenant.SigningKey, admin, adminToken, seededDevices);
    }

    /// <summary>
    /// Mints an unauthenticated user JWT with the supplied claims using the
    /// supplied signing key. When <paramref name="signingKeyOverride"/> is null,
    /// reads the production-test key from configuration so the JwtBearer
    /// middleware accepts the resulting token. Pen-tests pass an explicit wrong
    /// key to verify rejection paths.
    /// </summary>
    public static string ForgeUserJwt(
        ApiTestFactory factory,
        IEnumerable<Claim> claims,
        DateTime? expiresAt              = null,
        string? signingKeyOverride       = null,
        string? issuerOverride           = null,
        string? audienceOverride         = null)
    {
        var config   = factory.Services.GetRequiredService<IConfiguration>();
        var key      = signingKeyOverride ?? config["Jwt:Key"]!;
        var issuer   = issuerOverride     ?? config["Jwt:Issuer"];
        var audience = audienceOverride   ?? config["Jwt:Audience"];

        var symKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds  = new SigningCredentials(symKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            expiresAt ?? DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Builds the standard claim set for a user JWT (sub, NameIdentifier,
    /// email, tenantId, role, type). Caller may add or replace claims via the
    /// returned mutable list. Mirrors <see cref="TokenService.CreateUserToken"/>
    /// so the JwtBearer middleware accepts it on the happy path.
    /// </summary>
    public static List<Claim> StandardUserClaims(Guid userId, Guid tenantId, string email, UserRole role)
    {
        return new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier,    userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("tenantId",                    tenantId.ToString()),
            new("role",                        role.ToString()),
            new("type",                        "user"),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };
    }

    /// <summary>
    /// Builds the standard claim set for a device JWT.
    /// </summary>
    public static List<Claim> StandardDeviceClaims(Guid deviceId, Guid tenantId)
    {
        return new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
            new("tenantId",                    tenantId.ToString()),
            new("deviceId",                    deviceId.ToString()),
            new("type",                        "device"),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };
    }

    /// <summary>
    /// Returns an authenticated <see cref="HttpClient"/> bearing the supplied
    /// JWT. The factory's <c>CreateClient</c> base address points at TestServer
    /// so all requests stay in-process.
    /// </summary>
    public static HttpClient AuthedClient(ApiTestFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
