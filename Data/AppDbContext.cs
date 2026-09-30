using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TenNet.Domain;

namespace TenNet.Data;

public sealed class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    public bool BypassTenantFilter { get; set; }
    public bool IsSeedingMode { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        if (tenantContext is null)
        {
            _tenantContext = new UnsetTenantContext();
            BypassTenantFilter = true;
        }
        else
        {
            _tenantContext = tenantContext;
            BypassTenantFilter = false;
        }
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserTenantMembership> UserTenantMemberships => Set<UserTenantMembership>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<ItemFile> ItemFiles => Set<ItemFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

        var items = modelBuilder.Entity<InventoryItem>();
        items.HasKey(x => x.Id);
        items.HasAlternateKey(x => new { x.TenantId, x.Id });
        items.Property(x => x.Name).HasMaxLength(200).IsRequired();
        items.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        items.Property(x => x.Price).HasPrecision(18, 2);
        items.Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        items.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        items.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);

        var files = modelBuilder.Entity<ItemFile>();
        files.HasKey(x => x.Id);
        files.Property(x => x.OriginalName).HasMaxLength(255).IsRequired();
        files.Property(x => x.StoredKey).HasMaxLength(512).IsRequired();
        files.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        files.HasIndex(x => x.StoredKey).IsUnique();
        files.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        files.HasOne(x => x.Item).WithMany(x => x.Files)
            .HasForeignKey(x => new { x.TenantId, x.ItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });

        var audits = modelBuilder.Entity<AuditLog>();
        audits.HasKey(x => x.Id);
        audits.Property(x => x.Action).HasMaxLength(32).IsRequired();
        audits.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        audits.HasIndex(x => new { x.TenantId, x.CreatedAt });
        audits.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        audits.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;

        // Apply reflection-based global query filter to every entity implementing ITenantEntity
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)?
                    .MakeGenericMethod(entityType.ClrType);
                method?.Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ConfigureTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            BypassTenantFilter ||
            IsSeedingMode ||
            (_tenantContext.IsSet && e.TenantId == (_tenantContext.TenantId ?? Guid.Empty)));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        var now = DateTime.UtcNow;

        if (!BypassTenantFilter && !IsSeedingMode)
        {
            try
            {
                ChangeTracker.DetectChanges();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("TenantId") && ex.Message.Contains("cannot be modified"))
            {
                throw new TenantViolationException("TenantId is immutable and cannot be modified.", ex);
            }

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (!_tenantContext.IsSet || !_tenantContext.TenantId.HasValue)
                    {
                        throw new TenantViolationException("Cannot save tenant entity without an active tenant context.");
                    }

                    // Stamp TenantId using current tenant context, completely ignoring client-supplied values
                    entry.Entity.TenantId = _tenantContext.TenantId.Value;
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    if (!_tenantContext.IsSet || !_tenantContext.TenantId.HasValue)
                    {
                        throw new TenantViolationException("Cannot modify or delete tenant entity without an active tenant context.");
                    }

                    // Reject cross-tenant modification or deletion
                    if (entry.Entity.TenantId != _tenantContext.TenantId.Value)
                    {
                        throw new TenantViolationException("Cross-tenant modification or deletion is strictly prohibited.", entry.Entity.TenantId, _tenantContext.TenantId.Value);
                    }

                    // Reject attempts to change TenantId
                    if (entry.State == EntityState.Modified &&
                        entry.Property(x => x.TenantId).IsModified &&
                        !Equals(entry.Property(x => x.TenantId).OriginalValue, entry.Property(x => x.TenantId).CurrentValue))
                    {
                        throw new TenantViolationException("TenantId is immutable and cannot be modified.", entry.Entity.TenantId, _tenantContext.TenantId.Value);
                    }
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<InventoryItem>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.Quantity < 0 || entry.Entity.Price < 0 ||
                entry.Entity.Price > 9999999999999999.99m ||
                decimal.Round(entry.Entity.Price, 2) != entry.Entity.Price)
                throw new InvalidOperationException("Inventory quantity or price is invalid.");

            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            else entry.Property(x => x.CreatedAt).IsModified = false;
            entry.Entity.UpdatedAt = now;
            entry.Entity.RowVersion = Guid.NewGuid();
        }

        foreach (var entry in ChangeTracker.Entries<ItemFile>().Where(x => x.State == EntityState.Added))
            entry.Entity.CreatedAt = now;
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(x => x.State == EntityState.Added))
            entry.Entity.CreatedAt = now;
    }

    private sealed class UnsetTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public string? TenantName => null;
        public bool IsSet => false;
    }
}
