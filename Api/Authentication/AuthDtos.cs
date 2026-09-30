using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TenNet.Api.Authentication;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed record TenantSummary(Guid Id, string Name);
public sealed record UserSummary(Guid Id, string Email, string UserName);
public sealed record MeResponse(UserSummary User, IReadOnlyList<TenantSummary> Tenants);
public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAt,
    UserSummary User, IReadOnlyList<TenantSummary> Tenants);
