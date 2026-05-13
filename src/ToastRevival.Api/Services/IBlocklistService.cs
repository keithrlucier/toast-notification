using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface IBlocklistService
{
    Task<ModerationResult?> CheckAsync(
        string title, string? bodyLine1, string? bodyLine2, CancellationToken ct = default);
}
