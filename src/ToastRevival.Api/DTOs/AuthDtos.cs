using System.ComponentModel.DataAnnotations;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

// DC-L5: RegisterRequest removed — Register() action deleted (was dead 410 code).

// Two-step registration flow
public record RegisterInitRequest(
    [Required] string FullName,
    [Required] string TenantName,
    [Required, EmailAddress] string Email,
    [Required, Phone] string Mobile,
    string? Subdomain = null);

public record RegisterInitResponse(Guid UserId, string Step);

public record PublicRegistrationConfigResponse(
    bool TurnstileEnabled,
    string? TurnstileSiteKey);

public record TrialRegistrationRequest(
    [Required, MaxLength(200)] string CompanyName,
    [Required, MaxLength(500)] string Website,
    [Required, MaxLength(160)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, Phone, MaxLength(64)] string Phone,
    [Required, MaxLength(160)] string JobTitle,
    [Required] TrialUseCase IntendedUseCase,
    [MaxLength(2000)] string? IntendedUseCaseDetails,
    string? TurnstileToken);

public record TrialRegistrationResponse(
    Guid RequestId,
    string Step,
    string Message);

public record VerifySmsRequest(
    [Required] Guid UserId,
    [Required] string Code);

// AA-M3: MinLength updated to 12 to match hardened password policy.
public record SetPasswordRequest(
    [Required] Guid UserId,
    [Required] string Token,
    [Required, MinLength(12)] string Password);

public record ForgotPasswordRequest(
    [Required, EmailAddress] string Email);

// AA-M3: MinLength updated to 12 to match hardened password policy.
public record ResetPasswordRequest(
    [Required] Guid UserId,
    [Required] string Token,
    [Required, MinLength(12)] string Password);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

// SMS MFA challenge returned by Login when PhoneNumberConfirmed == true
public record LoginSmsChallenge(Guid UserId, string Step, string MaskedPhone);

// SMS OTP verify — completes login
public record LoginSmsVerifyRequest(
    [Required] Guid UserId,
    [Required] string Code);

// Authenticator (TOTP) challenge returned by Login when the user has a confirmed
// MfaSecret. Takes precedence over the SMS challenge. Step == "totp_required".
public record LoginTotpChallenge(Guid UserId, string Step);

// Authenticator OTP verify — completes login
public record LoginTotpVerifyRequest(
    [Required] Guid UserId,
    [Required] string Code);

// AA-M1: RefreshToken removed — it was generated but never stored in DB and
// had no exchange endpoint. The Dashboard frontend (authApi.register) cleanup
// is handled by the Frontend agent. Token is the session JWT.
public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role,
    bool IsPlatformAdmin);
