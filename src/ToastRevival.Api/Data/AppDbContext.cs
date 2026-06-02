using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

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
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<TrialRequest> TrialRequests => Set<TrialRequest>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
            e.HasQueryFilter(d => d.TenantId == _tenantProvider.TenantId);
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

        builder.Entity<TenantApiKey>(e =>
        {
            e.HasOne(k => k.Tenant)
             .WithMany()
             .HasForeignKey(k => k.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(k => k.Name).HasMaxLength(100);
            e.Property(k => k.KeyPrefix).HasMaxLength(16);
            e.Property(k => k.KeyHash).HasMaxLength(64);
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.HasQueryFilter(k => k.TenantId == _tenantProvider.TenantId);
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
