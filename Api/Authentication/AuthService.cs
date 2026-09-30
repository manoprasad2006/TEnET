using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Authentication;

public sealed class AuthService(AppDbContext db, JwtSettings jwt, LoginPasswordVerifier passwords)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var valid = passwords.Verify(user, request.Password);

        if (!valid)
        {
            var remaining = TimeSpan.FromMilliseconds(500) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            return null;
        }

        var tenants = await GetTenantsAsync(user!.Id, cancellationToken);
        var now = DateTime.UtcNow;
        var expires = now.Add(JwtSettings.Lifetime);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
             new Claim(JwtRegisteredClaimNames.Email, user.Email)],
            notBefore: now, expires: expires,
            signingCredentials: new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires,
            Summarize(user), tenants);
    }

    public async Task<MeResponse?> MeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return user is null ? null : new MeResponse(Summarize(user), await GetTenantsAsync(user.Id, cancellationToken));
    }

    private async Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Bound the query and fail explicitly rather than silently hiding memberships.
        var tenants = await db.UserTenantMemberships.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Tenant.Name).ThenBy(x => x.TenantId)
            .Select(x => new TenantSummary(x.TenantId, x.Tenant.Name)).Take(101).ToListAsync(cancellationToken);
        if (tenants.Count > 100) throw new InvalidOperationException("Membership response limit exceeded.");
        return tenants;
    }

    private static UserSummary Summarize(AppUser user) => new(user.Id, user.Email, user.UserName);
}
