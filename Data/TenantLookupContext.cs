using Microsoft.EntityFrameworkCore;
using TenNet.Domain;

namespace TenNet.Data;

public sealed class TenantLookupContext(DbContextOptions<TenantLookupContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserTenantMembership> UserTenantMemberships => Set<UserTenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenants = modelBuilder.Entity<Tenant>();
        tenants.HasKey(x => x.Id);
        tenants.Property(x => x.Name).HasMaxLength(100).IsRequired();
        tenants.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        tenants.HasIndex(x => x.Slug).IsUnique();

        var users = modelBuilder.Entity<AppUser>();
        users.HasKey(x => x.Id);
        users.Property(x => x.UserName).HasMaxLength(64).IsRequired();
        users.Property(x => x.Email).HasMaxLength(254).IsRequired();
        users.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        users.HasIndex(x => x.Email).IsUnique();
        users.HasIndex(x => x.UserName).IsUnique();

        var memberships = modelBuilder.Entity<UserTenantMembership>();
        memberships.HasKey(x => new { x.UserId, x.TenantId });
        memberships.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        memberships.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
