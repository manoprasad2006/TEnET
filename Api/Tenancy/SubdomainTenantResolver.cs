using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TenNet.Api.Tenancy;

public sealed class SubdomainOptions
{
    public const string SectionName = "Tenancy:Subdomain";

    public bool Enabled { get; set; } = false;
    public string BaseDomain { get; set; } = "example.com";
}

/// <summary>
/// Disabled stub for subdomain-based tenant resolution.
/// Configuration:
/// - Set "Tenancy:Subdomain:Enabled" to true (default: false).
/// - Set "Tenancy:Subdomain:BaseDomain" to the root domain (e.g. "tennet.example" or "localhost").
/// When enabled, this resolver extracts the tenant slug from the host header (e.g. "acme.tennet.example" -> "acme").
/// </summary>
public sealed class SubdomainTenantResolver(IOptions<SubdomainOptions> options, ILogger<SubdomainTenantResolver> logger) : ITenantResolver
{
    private readonly SubdomainOptions _options = options.Value;

    public Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            // Subdomain resolution is disabled by default in configuration.
            return Task.FromResult(TenantResolutionResult.Missing("Subdomain tenant resolution is disabled."));
        }

        var host = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(TenantResolutionResult.Missing("Host header is missing."));
        }

        if (!host.EndsWith(_options.BaseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Host '{host}' does not match configured base domain '{_options.BaseDomain}'."));
        }

        var prefixLength = host.Length - _options.BaseDomain.Length;
        if (prefixLength <= 1)
        {
            return Task.FromResult(TenantResolutionResult.Missing("No subdomain prefix found on host."));
        }

        var subdomain = host[..(prefixLength - 1)].Trim();
        if (string.IsNullOrWhiteSpace(subdomain) || subdomain.Contains('.'))
        {
            return Task.FromResult(TenantResolutionResult.Invalid("Malformed subdomain format."));
        }

        logger.LogInformation("Subdomain tenant '{Subdomain}' detected (stub mode).", subdomain);
        return Task.FromResult(TenantResolutionResult.Missing($"Subdomain '{subdomain}' identified but resolver stub does not map slugs to GUIDs yet."));
    }
}
