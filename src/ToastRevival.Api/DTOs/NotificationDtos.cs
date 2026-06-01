using System.ComponentModel.DataAnnotations;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

public record SendNotificationRequest(
    [Required, MaxLength(64)] string Title,
    [MaxLength(128)] string? BodyLine1 = null,
    [MaxLength(128)] string? BodyLine2 = null,
    [AbsoluteHttpUrl] string? HeroImageUrl = null,
    [AbsoluteHttpUrl] string? LogoUrl = null,
    object? ActionButtons = null,
    string? AudioSetting = null,
    ToastScenario Scenario = ToastScenario.Default,
    TargetType TargetType = TargetType.All,
    [MaxLength(1000)] IList<Guid>? TargetIds = null,
    Guid? TemplateId = null,
    DateTime? ScheduledAt = null);

/// <summary>
/// Validates that a string, when present, is an absolute http(s) URL. Blocks
/// UNC paths, file://, and other schemes that would otherwise be HMAC-signed
/// and fetched by the Windows agent (SSRF / NetNTLM-leak surface). Null/empty
/// is allowed — the field is optional.
/// </summary>
public sealed class AbsoluteHttpUrlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return ValidationResult.Success;

        if (Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
            return ValidationResult.Success;

        return new ValidationResult(
            $"{context.DisplayName} must be an absolute http:// or https:// URL.");
    }
}

public record NotificationResponse(
    Guid Id,
    string Title,
    string? BodyLine1,
    string? BodyLine2,
    string Status,
    string TargetType,
    int TargetDeviceCount,
    DateTime? ScheduledAt,
    DateTime? SentAt,
    DateTime CreatedAt);

public record NotificationHistoryItem(
    Guid Id,
    string Title,
    string Status,
    int TargetDeviceCount,
    int DeliveredCount,
    int ClickedCount,
    DateTime CreatedAt,
    DateTime? SentAt);

/// <summary>
/// One pending delivery returned from GET /api/notifications/pending. Same wire
/// shape the hub fanout uses (payloadJson + signature) — agent runs the same
/// HMAC verification path regardless of which channel delivered the payload.
/// </summary>
public record PendingNotificationItem(
    Guid NotificationId,
    string PayloadJson,
    string Signature,
    DateTime CreatedAt);
