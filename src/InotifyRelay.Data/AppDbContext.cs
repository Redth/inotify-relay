using InotifyRelay.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RuleEntity> Rules => Set<RuleEntity>();
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<TargetEntity> Targets => Set<TargetEntity>();
    public DbSet<TargetBindingEntity> TargetBindings => Set<TargetBindingEntity>();
    public DbSet<EventLogEntity> EventLogs => Set<EventLogEntity>();
    public DbSet<DeliveryLogEntity> DeliveryLogs => Set<DeliveryLogEntity>();
    public DbSet<AuthSettingsEntity> AuthSettings => Set<AuthSettingsEntity>();
    public DbSet<SystemSettingsEntity> SystemSettings => Set<SystemSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<RuleEntity>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Sources).WithOne(x => x.Rule).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.TargetBindings).WithOne(x => x.Rule).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TargetEntity>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<TargetBindingEntity>(e =>
        {
            e.HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EventLogEntity>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.RuleId);
        });

        b.Entity<DeliveryLogEntity>(e =>
        {
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.TargetId);
            e.HasIndex(x => x.EventLogId);
        });

    }
}
