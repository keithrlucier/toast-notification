namespace ToastRevival.Api.DTOs;

public record TemplateResponse(
    Guid Id,
    string Name,
    string Slug,
    string Category,
    string? TitleTemplate,
    string? BodyLine1Template,
    string? BodyLine2Template,
    string? HeroImageUrl,
    string? LogoImageUrl,
    string? ActionButtonsJson,
    string? AudioSetting,
    string Scenario,
    bool IsDefault);

// INJ-M1: AbsoluteHttpUrl validation added to HeroImageUrl and LogoImageUrl.
public record CreateTemplateRequest(
    string Name,
    string? Title,
    string? BodyLine1,
    string? BodyLine2,
    [AbsoluteHttpUrl] string? HeroImageUrl,
    [AbsoluteHttpUrl] string? LogoImageUrl,
    string? ActionButtonsJson,
    string? AudioSetting,
    string? Scenario);

// INJ-M1: AbsoluteHttpUrl validation added to HeroImageUrl and LogoImageUrl.
public record UpdateTemplateRequest(
    string Name,
    string? Title,
    string? BodyLine1,
    string? BodyLine2,
    [AbsoluteHttpUrl] string? HeroImageUrl,
    [AbsoluteHttpUrl] string? LogoImageUrl,
    string? ActionButtonsJson,
    string? AudioSetting,
    string? Scenario);
