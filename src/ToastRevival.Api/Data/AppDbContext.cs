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
            // INFO-M2B-003: composite index for the catch-up query (DeviceId, Status, CreatedAt)
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
        // INFO-M5D-002: index on Timestamp for export/range queries.
        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => new { a.TenantId, a.Timestamp });
            e.HasIndex(a => a.Timestamp);
        });

        // INFO-M5C-002: partial index on (Status, ScheduledAt) for scheduler sweep
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
