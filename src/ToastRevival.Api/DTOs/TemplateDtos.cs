namespace ToastRevival.Api.DTOs;

public record TemplateResponse(
    Guid Id,
    string Name,
    string Slug,
    string Category,
    string? TitleTemplate,
    string? BodyLine1Template,
    string? BodyLine2Template,
    string? AudioSetting,
    string Scenario,
    bool IsDefault);
