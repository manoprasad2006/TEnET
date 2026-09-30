using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TenNet.Domain;

namespace TenNet.Api.Tenancy;

public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    private static readonly HashSet<string> AnonymousAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "/hello",
        "/health",
        "/api/auth/login",
        "/api/auth/me"
    };

    public async Task InvokeAsync(
        HttpContext context,
        CompositeTenantResolver resolver,
        ITenantLookupService lookupService,
        ITenantContextSetter contextSetter)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        // Exempt endpoints bypass tenant resolution
        if (AnonymousAllowlist.Contains(path) || IsAllowAnonymous(context))
        {
            await next(context);
            return;
        }

        // 1. Resolve tenant identifier from request
        TenantResolutionResult resolution;
        try
        {
            resolution = await resolver.ResolveAsync(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant resolver failed unexpectedly.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred during tenant resolution.")
                .ExecuteAsync(context);
            return;
        }

        if (resolution.Status == TenantResolutionStatus.Missing)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: resolution.ErrorMessage ?? "Missing required tenant identifier.")
                .ExecuteAsync(context);
            return;
        }

        if (resolution.Status == TenantResolutionStatus.Invalid)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: resolution.ErrorMessage ?? "Invalid tenant identifier.")
                .ExecuteAsync(context);
            return;
        }

        var tenantId = resolution.TenantId!.Value;

        // 2. Verify authentication
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
                .ExecuteAsync(context);
            return;
        }

        var subClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(subClaim, out var userId))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
                .ExecuteAsync(context);
            return;
        }

        // 3. Lookup tenant and verify user membership
        TenantLookupResult lookup;
        try
        {
            lookup = await lookupService.ValidateTenantMembershipAsync(userId, tenantId, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during tenant lookup and membership validation.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred during tenant resolution.")
                .ExecuteAsync(context);
            return;
        }

        if (!lookup.Exists)
        {
            // Unknown tenant returns 404
            await Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Tenant not found")
                .ExecuteAsync(context);
            return;
        }

        if (!lookup.IsMember)
        {
            // Non-member tenant returns 403
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "User is not a member of the requested tenant.")
                .ExecuteAsync(context);
            return;
        }

        // 4. Set scoped tenant context (throws if set twice)
        try
        {
            contextSetter.SetTenant(lookup.Tenant!.Id, lookup.Tenant.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set tenant context.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred while setting tenant context.")
                .ExecuteAsync(context);
            return;
        }

        // 5. Establish logging scope with TenantId and UserId (no secrets)
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = lookup.Tenant!.Id,
            ["TenantName"] = lookup.Tenant.Name,
            ["UserId"] = userId
        }))
        {
            await next(context);
        }
    }

    private static bool IsAllowAnonymous(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
