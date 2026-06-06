namespace ToastRevival.Api.Services;

public interface ITenantProvider
{
    // Nullable — unauthenticated requests (login, register, device registration) have no tenant yet.
    // IMPORTANT: null TenantId means no tenant context — global query filters return zero rows,
    // NOT unfiltered data. EF Core evaluates (column == null) as SQL NULL comparison which matches
    // nothing in tenant-owned tables. Background services must use IgnoreQueryFilters() for
    // cross-tenant reads; never rely on null TenantId to bypass isolation.
    Guid? TenantId { get; }
}
