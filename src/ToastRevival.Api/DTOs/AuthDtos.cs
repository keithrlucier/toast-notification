using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

public record RegisterRequest(
    [Required] string TenantName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    string? Subdomain = null,
    string? DisplayName = null);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record AuthResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role);
