using System.ComponentModel.DataAnnotations;

namespace ToastRevival.Api.DTOs;

public record CreateApiKeyRequest([Required, MaxLength(100)] string Name);

public record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    string FullKey,        // returned ONCE at creation — never stored
    DateTime CreatedAt);

public record ApiKeyResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool IsRevoked);
