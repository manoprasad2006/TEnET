using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public sealed class CompositeTenantResolver(IEnumerable<ITenantResolver> resolvers) : ITenantResolver
{
    private readonly IReadOnlyList<ITenantResolver> _resolvers = resolvers.ToList();

    public async Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        TenantResolutionResult? firstMissing = null;

        foreach (var resolver in _resolvers)
        {
            var result = await resolver.ResolveAsync(context);
            if (result.Status == TenantResolutionStatus.Success)
            {
                return result;
            }

            if (result.Status == TenantResolutionStatus.Invalid)
            {
                // Fail closed immediately on malformed, duplicate, or oversized inputs
                return result;
            }

            firstMissing ??= result;
        }

        return firstMissing ?? TenantResolutionResult.Missing("Missing required tenant identifier.");
    }
}
