namespace TenNet.Domain;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantName { get; }
    bool IsSet { get; }
}

public interface ITenantContextSetter
{
    void SetTenant(Guid tenantId, string tenantName);
}

public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }
    public string? TenantName { get; private set; }
    public bool IsSet { get; private set; }

    public void SetTenant(Guid tenantId, string tenantName)
    {
        if (IsSet)
        {
            throw new InvalidOperationException("Tenant context has already been set for this request.");
        }
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        }
        if (string.IsNullOrWhiteSpace(tenantName))
        {
            throw new ArgumentException("TenantName cannot be null or whitespace.", nameof(tenantName));
        }

        TenantId = tenantId;
        TenantName = tenantName.Trim();
        IsSet = true;
    }
}
