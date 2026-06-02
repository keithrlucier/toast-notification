using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

// XT-1 — admin enrollment-token management DTOs.

public record IssueEnrollmentTokenRequest(
    [MaxLength(120)] string? Label = null,
    // Optional override for the unredeemed-token TTL. Clamped server-side to
    // [1, 168] hours; null uses the 24h default.
    int? TtlHours = null);

// Returned ONCE at issue time — this is the only moment the plaintext token
// exists outside the agent that will redeem it.
public record IssuedEnrollmentTokenResponse(
    Guid Id,
    string Token,
    DateTime ExpiresAt,
    string? Label);

// List view. Never carries the plaintext token. Status is computed server-side:
// "revoked" > "used" > "expired" > "active".
public record EnrollmentTokenDto(
    Guid Id,
    string? Label,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string? UsedByDeviceName,
    string? UsedByUsername,
    DateTime? RevokedAt);
