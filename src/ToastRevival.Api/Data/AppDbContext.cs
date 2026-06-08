using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

// DC-M4 — Migrations are CONSOLIDATED (2026-06-07): a single tree at Data/Migrations/ under
// the one namespace ToastRevival.Api.Data.Migrations. The full history (InitialCreate ->
// M18_StripeWebhookInbox) plus the lone AppDbContextModelSnapshot all live there. The legacy
// Migrations/ folder (namespace ToastRevival.Api.Migrations) that historically held M1-M8 +
// the snapshot was removed; its files were git-mv'd here and renamespaced. EF identifies a
// migration by its [Migration("id")] attribute, not its namespace/folder, so the move left
// __EFMigrationsHistory and all applied state untouched. Standing rule: every new migration
// goes in Data/Migrations/ with namespace ToastRevival.Api.Data.Migrations (`dotnet ef
// migrations add <Name> -o Data/Migrations`). There is no second tree to drift against.
namespace ToastRevival.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();
    public DbSet<DeviceGroupMember> DeviceGroupMembers => Set<DeviceGroupMember>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AssetLibrary> AssetLibrary => Set<AssetLibrary>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TenantBlocklistEntry> TenantBlocklistEntries => Set<TenantBlocklistEntry>();
    // DC-H1: TenantApiKey (dead table) removed — DbSet and entity config dropped.
    public DbSet<TrialRequest> TrialRequests => Set<TrialRequest>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    // REL-003-R: Stripe webhook inbox — durable event log for exactly-once processing.
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // MT-C1 — HasQueryFilter safety note: when _tenantProvider.TenantId is null (unauthenticated
        // or background context with no tenant set), every HasQueryFilter below evaluates as
        // (column == null) which produces SQL "WHERE column IS NULL" — zero rows, not unfiltered
        // data. This is intentional: a missing tenant context produces empty results rather than
        // leaking cross-tenant data. Background services that need cross-tenant reads MUST call
        // IgnoreQueryFilters() explicitly.

        builder.Entity<AppUser>(e =>
        {
            e.HasOne(u => u.Tenant)
             .WithMany(t => t.Users)
             .HasForeignKey(u => u.TenantId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(u => u.TenantId == _tenantProvider.TenantId);
        });

        builder.Entity<Device>(e =>
        {
            e.HasOne(d => d.Tenant)
             .WithMany(t => t.Devices)
             .HasForeignKey(d => d.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => d.RegistrationToken).IsUnique();
            // M1 — bound IP columns to 64 chars (covers IPv4 + full IPv6 w/ zone ID).
            e.Property(d => d.WanIpAddress).HasMaxLength(64);
            e.Property(d => d.LanIpAddress).HasMaxLength(64);
            // MachineGuid identity (collector phase) — bound the two new signal columns.
            e.Property(d => d.MachineGuid).HasMaxLength(64);
            e.Property(d => d.DnsHostName).HasMaxLength(256);
            e.HasQueryFilter(d => d.TenantId == _tenantProvider.TenantId);
            // PERF-L2: tenant+status composite for device list / active-device count queries.
            e.HasIndex(d => new { d.TenantId, d.Status }).HasDatabaseName("IX_Devices_TenantId_Status");
            // MachineGuid identity — NON-unique on purpose. In the collector phase a
            // MachineGuid may legitimately repeat across factory-cloned (non-sysprepped)
            // boxes; a UNIQUE index would reject their register/ping writes and break
            // enrollment. This index backs both the lookup a future merge will use and the
            // duplicate-rate analysis that decides whether MachineGuid is a safe sole key.
            e.HasIndex(d => new { d.TenantId, d.MachineGuid }).HasDatabaseName("IX_Devices_TenantId_MachineGuid");
        });

        builder.Entity<DeviceGroup>(e =>
        {
            e.HasOne(g => g.Tenant)
             .WithMany(t => t.DeviceGroups)
             .HasForeignKey(g => g.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(g => g.TenantId == _tenantProvider.TenantId);
        });

        builder.Entity<DeviceGroupMember>(e =>
        {
            e.HasKey(m => new { m.DeviceGroupId, m.DeviceId });
            e.HasOne(m => m.DeviceGroup)
             .WithMany(g => g.Members)
             .HasForeignKey(m => m.DeviceGroupId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Device)
             .WithMany(d => d.GroupMemberships)
             .HasForeignKey(m => m.DeviceId)
             .OnDelete(DeleteBehavior.Cascade);
            // DGM-M2 — scope through the required DeviceGroup navigation so a direct
            // _db.DeviceGroupMembers query is tenant-isolated like every other
            // tenant-associated entity (DeviceGroupMember has no own TenantId column).
            // REVIEW-2026-06-06 MT-M1 REJECTED-by-design: join-based filter (m.DeviceGroup.TenantId == tenantId) is the EF Core owned-navigation pattern; adding a redundant TenantId discriminator creates a dual-write consistency risk; orphaned DeviceGroupMember rows are prevented by cascade-delete FK on DeviceGroupId
            e.HasQueryFilter(m => m.DeviceGroup.TenantId == _tenantProvider.TenantId);
        });

        builder.Entity<NotificationTemplate>(e =>
        {
            e.HasOne(t => t.Tenant)
             .WithMany()
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(t => t.TenantId == _tenantProvider.TenantId);
        });

        builder.Entity<Notification>(e =>
        {
            e.HasOne(n => n.Tenant)
             .WithMany(t => t.Notifications)
             .HasForeignKey(n => n.TenantId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Sender)
             .WithMany()
             .HasForeignKey(n => n.SenderId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(n => n.TenantId == _tenantProvider.TenantId);
            // PERF-L1: tenant-scoped list and range queries (dashboard, analytics).
            e.HasIndex(n => n.TenantId).HasDatabaseName("IX_Notifications_TenantId");
            e.HasIndex(n => new { n.TenantId, n.CreatedAt }).HasDatabaseName("IX_Notifications_TenantId_CreatedAt").IsDescending(false, true);
        });

        builder.Entity<NotificationDelivery>(e =>
        {
            e.HasOne(d => d.Notification)
             .WithMany(n => n.Deliveries)
             .HasForeignKey(d => d.NotificationId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Device)
             .WithMany(dev => dev.Deliveries)
             .HasForeignKey(d => d.DeviceId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.NotificationId, d.DeviceId }).IsUnique();
            // Composite index for the catch-up query (DeviceId, Status, CreatedAt).
            e.HasIndex(d => new { d.DeviceId, d.Status, d.CreatedAt });
            // PERF-L1: tenant-scoped delivery queries.
            e.HasIndex(d => d.TenantId).HasDatabaseName("IX_NotificationDeliveries_TenantId");
            // REVIEW-2026-06-06 PERF-L5 REJECTED-by-design: functional index on DATE(sent_at) requires PostgreSQL-specific DDL outside EF Core model conventions; acceptable at current analytics query volume, planned as a DBA migration when query plans show measurable regression
            e.HasQueryFilter(d => d.TenantId == _tenantProvider.TenantId);
        });

        builder.Entity<AssetLibrary>(e =>
        {
            e.HasOne(a => a.Tenant)
             .WithMany()
             .HasForeignKey(a => a.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(a => a.TenantId == _tenantProvider.TenantId);
        });

        // MT-M2: No global query filter by design — AuditLog must be queried with explicit
        // l.TenantId == tenantId predicates at every read site; any future endpoint missing
        // this predicate silently exposes cross-tenant audit history.
        // AuditLog intentionally has no global filter — admins can query across tenants.
        // Index on Timestamp supports export/range queries.
        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => new { a.TenantId, a.Timestamp });
            e.HasIndex(a => a.Timestamp);
        });

        // Partial index on (Status, ScheduledAt) for the scheduler sweep.
        builder.Entity<Notification>(e =>
        {
            e.HasIndex(n => new { n.Status, n.ScheduledAt })
             .HasFilter("scheduled_at IS NOT NULL");
        });

        builder.Entity<TenantBlocklistEntry>(e =>
        {
            e.HasOne(b => b.Tenant)
             .WithMany()
             .HasForeignKey(b => b.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(b => b.Term).HasMaxLength(500);
            e.HasIndex(b => new { b.TenantId, b.Term }).IsUnique();
            e.HasQueryFilter(b => b.TenantId == _tenantProvider.TenantId);
        });

        // XT-1 — per-device single-use enrollment tokens. Tenant-scoped like the
        // other per-tenant tables; a unique (TenantId, TokenHash) index backs the
        // O(1) lookup at registration time.
        builder.Entity<EnrollmentToken>(e =>
        {
            e.HasOne(t => t.Tenant)
             .WithMany()
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.Property(t => t.Label).HasMaxLength(120);
            e.Property(t => t.UsedByDeviceName).HasMaxLength(256);
            e.Property(t => t.UsedByUsername).HasMaxLength(256);
            e.HasIndex(t => new { t.TenantId, t.TokenHash }).IsUnique();
            e.HasQueryFilter(t => t.TenantId == _tenantProvider.TenantId);
        });

        // MT-L1: No global query filter — TrialRequest is a platform-level entity created before
        // a tenant exists; controller must add explicit TenantId predicates when needed.
        builder.Entity<TrialRequest>(e =>
        {
            e.Property(r => r.CompanyName).HasMaxLength(200);
            e.Property(r => r.Website).HasMaxLength(500);
            e.Property(r => r.FullName).HasMaxLength(160);
            e.Property(r => r.Email).HasMaxLength(256);
            e.Property(r => r.Phone).HasMaxLength(64);
            e.Property(r => r.JobTitle).HasMaxLength(160);
            e.Property(r => r.IntendedUseCaseDetails).HasMaxLength(2000);
            e.Property(r => r.ReviewNote).HasMaxLength(1000);
            e.Property(r => r.RemoteIpAddress).HasMaxLength(64);
            e.Property(r => r.UserAgent).HasMaxLength(512);
            e.Property(r => r.TurnstileHostname).HasMaxLength(255);
            e.Property(r => r.TurnstileAction).HasMaxLength(100);
            e.HasIndex(r => new { r.Status, r.SubmittedAt });
            e.HasIndex(r => r.Email);
        });

        // REL-003-R: Stripe webhook inbox. No tenant filter — this is platform-level state.
        // EventId unique index enforces exactly-once processing on Stripe replays.
        builder.Entity<StripeWebhookEvent>(e =>
        {
            e.Property(s => s.EventId).HasMaxLength(128);
            e.Property(s => s.EventType).HasMaxLength(128);
            e.Property(s => s.Status).HasMaxLength(32);
            e.HasIndex(s => s.EventId).IsUnique().HasDatabaseName("IX_StripeWebhookEvents_EventId");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Tenant>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = now;
        foreach (var entry in ChangeTracker.Entries<NotificationTemplate>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = now;
        return base.SaveChangesAsync(ct);
    }
}
