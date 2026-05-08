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
    TargetType TargetType,
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
