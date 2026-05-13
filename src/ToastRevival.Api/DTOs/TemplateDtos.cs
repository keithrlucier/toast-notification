namespace ToastRevival.Api.DTOs;

public record TemplateResponse(
    Guid Id,
    string Name,
    string Slug,
    string Category,
    string? TitleTemplate,
    string? BodyLine1Template,
    string? BodyLine2Template,
    string? ActionButtonsJson,
    string? AudioSetting,
    string Scenario,
    bool IsDefault);

public record CreateTemplateRequest(
    string Name,
    string? Title,
    string? BodyLine1,
    string? BodyLine2,
    string? ActionButtonsJson,
    string? AudioSetting,
    string? Scenario);
