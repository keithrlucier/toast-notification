using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

public record PendingReviewItem(
    Guid NotificationId,
    string Title,
    string? BodyLine1,
    string? BodyLine2,
    string? HeroImageUrl,
    string? ModerationResultJson,
    DateTime CreatedAt,
    string SenderEmail);

public record ModerationActionRequest(string? Reason);
