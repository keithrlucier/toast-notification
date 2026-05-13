namespace ToastRevival.Api.Models;

public class TrialRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CompanyName { get; set; } = null!;
    public string Website { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string JobTitle { get; set; } = null!;
    public TrialUseCase IntendedUseCase { get; set; }
    public string? IntendedUseCaseDetails { get; set; }

    public TrialRequestStatus Status { get; set; } = TrialRequestStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }

    public Guid? CreatedTenantId { get; set; }
    public Guid? CreatedUserId { get; set; }

    public string? RemoteIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? TurnstileHostname { get; set; }
    public string? TurnstileAction { get; set; }
}
