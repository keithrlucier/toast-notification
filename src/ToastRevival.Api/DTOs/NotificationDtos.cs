using System.ComponentModel.DataAnnotations;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

public record SendNotificationRequest(
    [Required, MaxLength(64)] string Title,
    [MaxLength(128)] string? BodyLine1 = null,
    [MaxLength(128)] string? BodyLine2 = null,
    string? HeroImageUrl = null,
    string? LogoUrl = null,
    object? ActionButtons = null,
    string? AudioSetting = null,
    ToastScenario Scenario = ToastScenario.Default,
    TargetType TargetType = TargetType.All,
    IList<Guid>? TargetIds = null,
    Guid? TemplateId = null,
    DateTime? ScheduledAt = null);

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
