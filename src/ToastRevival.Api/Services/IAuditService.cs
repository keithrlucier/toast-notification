namespace ToastRevival.Api.Services;

public interface IAuditService
{
    Task LogAsync(Guid tenantId, Guid? userId, string action, string resourceType,
        string? resourceId = null, object? details = null, string? ipAddress = null);
}
