namespace ToastRevival.Api.Models;

public class NotificationTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public TemplateCategory Category { get; set; }
    public string? TitleTemplate { get; set; }
    public string? BodyLine1Template { get; set; }
    public string? BodyLine2Template { get; set; }
    public Guid? HeroImageId { get; set; }
    public Guid? LogoImageId { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? LogoImageUrl { get; set; }
    public string? ActionButtonsJson { get; set; }
    public string? AudioSetting { get; set; }
    public ToastScenario Scenario { get; set; } = ToastScenario.Default;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
