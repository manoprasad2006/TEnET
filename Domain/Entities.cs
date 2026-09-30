namespace TenNet.Domain;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public sealed class UserTenantMembership
{
    // The membership has the explicitly requested composite primary key.
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public AppUser User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public sealed class InventoryItem : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid RowVersion { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public ICollection<ItemFile> Files { get; set; } = new List<ItemFile>();
}

public sealed class ItemFile : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public Guid TenantId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string StoredKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public InventoryItem Item { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public sealed class AuditLog : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
