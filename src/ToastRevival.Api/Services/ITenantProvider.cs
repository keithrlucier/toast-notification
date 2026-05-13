namespace ToastRevival.Api.Services;

public interface ITenantProvider
{
    // Nullable — unauthenticated requests (login, register, device registration) have no tenant yet.
    // Global query filters treat null as "no filter" or caller must use IgnoreQueryFilters() explicitly.
    Guid? TenantId { get; }
}
