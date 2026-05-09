namespace ToastRevival.Api.DTOs;

public record MfaEnrollResponse(string Secret, string QrUri);
public record MfaVerifyRequest(string Code);
public record MfaVerifyResponse(string MfaToken, DateTime ExpiresAt);
