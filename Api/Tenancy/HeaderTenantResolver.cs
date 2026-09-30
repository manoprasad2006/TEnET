using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public sealed class HeaderTenantResolver : ITenantResolver
{
    public const string HeaderName = "X-Tenant-Id";
    public const int MaxHeaderLength = 64;

    public Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        if (values.Count == 0)
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        if (values.Count > 1)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Duplicate {HeaderName} header detected."));
        }

        var rawHeader = values[0];
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        // Detect comma-separated multiple GUID values (e.g. from reverse proxy header folding)
        if (rawHeader.Contains(',') && Guid.TryParse(rawHeader.Split(',')[0].Trim(), out _))
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Duplicate {HeaderName} header detected."));
        }

        var trimmed = rawHeader.Trim();
        if (trimmed.Length > MaxHeaderLength)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"{HeaderName} header exceeds maximum allowed length of {MaxHeaderLength} characters."));
        }

        if (trimmed.Length != 36 || !Guid.TryParseExact(trimmed, "D", out var tenantId) || tenantId == Guid.Empty)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Malformed or invalid GUID in {HeaderName} header."));
        }

        return Task.FromResult(TenantResolutionResult.Success(tenantId, trimmed));
    }
}
