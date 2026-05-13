using System.Text.Json;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public class AuditService : IAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task LogAsync(Guid tenantId, Guid? userId, string action, string resourceType,
        string? resourceId = null, object? details = null, string? ipAddress = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            DetailsJson = details is not null ? JsonSerializer.Serialize(details) : null,
            IpAddress = ipAddress,
        });

        await db.SaveChangesAsync();
    }
}
