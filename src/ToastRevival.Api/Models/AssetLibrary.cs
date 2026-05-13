namespace ToastRevival.Api.Models;

public class AssetLibrary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public AssetType Type { get; set; }
    public string Url { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? ModerationResultJson { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
