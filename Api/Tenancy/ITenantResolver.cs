using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public enum TenantResolutionStatus
{
    Success,
    Missing,
    Invalid
}

public sealed class TenantResolutionResult
{
    public TenantResolutionStatus Status { get; }
    public Guid? TenantId { get; }
    public string? RawIdentifier { get; }
    public string? ErrorMessage { get; }

    private TenantResolutionResult(TenantResolutionStatus status, Guid? tenantId, string? rawIdentifier, string? errorMessage)
    {
        Status = status;
        TenantId = tenantId;
        RawIdentifier = rawIdentifier;
        ErrorMessage = errorMessage;
    }

    public static TenantResolutionResult Success(Guid tenantId, string rawIdentifier) =>
        new(TenantResolutionStatus.Success, tenantId, rawIdentifier, null);

    public static TenantResolutionResult Missing(string message = "Tenant identifier is missing.") =>
        new(TenantResolutionStatus.Missing, null, null, message);

    public static TenantResolutionResult Invalid(string errorMessage) =>
        new(TenantResolutionStatus.Invalid, null, null, errorMessage);
}

public interface ITenantResolver
{
    Task<TenantResolutionResult> ResolveAsync(HttpContext context);
}
