using Microsoft.EntityFrameworkCore;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Tenancy;

public sealed record TenantLookupResult(bool Exists, bool IsMember, Tenant? Tenant);

public interface ITenantLookupService
{
    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantLookupResult> ValidateTenantMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class TenantLookupService(TenantLookupContext db) : ITenantLookupService
{
    public async Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await db.UserTenantMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);
    }

    public async Task<TenantLookupResult> ValidateTenantMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return new TenantLookupResult(Exists: false, IsMember: false, Tenant: null);
        }

        var isMember = await db.UserTenantMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

        return new TenantLookupResult(Exists: true, IsMember: isMember, Tenant: tenant);
    }
}
