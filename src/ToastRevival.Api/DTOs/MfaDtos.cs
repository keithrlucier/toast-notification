namespace ToastRevival.Api.DTOs;

public record MfaEnrollResponse(string Secret, string QrUri);
public record MfaVerifyRequest(string Code);
public record MfaVerifyResponse(string MfaToken, DateTime ExpiresAt);

// Drives the Security card and the force-enrollment gate.
//   Enabled:        the caller has a confirmed authenticator (login factor active).
//   TenantRequired: tenant-wide MFA enforcement is on for the caller's tenant.
//   HasPhone:       a confirmed phone number is on file (SMS fallback available).
public record MfaStatusResponse(bool Enabled, bool TenantRequired, bool HasPhone);
