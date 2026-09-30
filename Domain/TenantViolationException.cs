namespace TenNet.Domain;

public sealed class TenantViolationException : InvalidOperationException
{
    public Guid? AttemptedTenantId { get; }
    public Guid? CurrentTenantId { get; }

    public TenantViolationException(string message) : base(message) { }

    public TenantViolationException(string message, Exception innerException) : base(message, innerException) { }

    public TenantViolationException(string message, Guid? attemptedTenantId, Guid? currentTenantId) : base(message)
    {
        AttemptedTenantId = attemptedTenantId;
        CurrentTenantId = currentTenantId;
    }
}
