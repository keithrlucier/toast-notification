using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

// Legacy — kept for backwards compat; new flow uses RegisterInitRequest
public record RegisterRequest(
    [Required] string TenantName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    string? Subdomain = null,
    string? DisplayName = null);

// M9.A — new two-step registration flow
public record RegisterInitRequest(
    [Required] string FullName,
    [Required] string TenantName,
    [Required, EmailAddress] string Email,
    [Required, Phone] string Mobile,
    string? Subdomain = null);

public record RegisterInitResponse(Guid UserId, string Step);

public record VerifySmsRequest(
    [Required] Guid UserId,
    [Required] string Code);

public record SetPasswordRequest(
    [Required] Guid UserId,
    [Required] string Token,
    [Required, MinLength(8)] string Password);

public record ForgotPasswordRequest(
    [Required, EmailAddress] string Email);

public record ResetPasswordRequest(
    [Required] Guid UserId,
    [Required] string Token,
    [Required, MinLength(8)] string Password);

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
    string Role,
    bool IsPlatformAdmin);
