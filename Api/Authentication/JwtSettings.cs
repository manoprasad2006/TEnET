using Microsoft.IdentityModel.Tokens;

namespace TenNet.Api.Authentication;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required SymmetricSecurityKey SigningKey { get; init; }
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(15);
    public static TimeSpan ClockSkew => TimeSpan.FromSeconds(30);

    public static JwtSettings Load(IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 200 ||
            string.IsNullOrWhiteSpace(audience) || audience.Length > 200)
            throw new InvalidOperationException("Configure Jwt:Issuer and Jwt:Audience.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuration["Jwt:SigningKey"] ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be a Base64-encoded random key.");
        }
        if (key.Length < 32)
            throw new InvalidOperationException("Set Jwt:SigningKey to at least 32 random bytes, Base64-encoded, using user-secrets or environment variables.");

        return new JwtSettings { Issuer = issuer, Audience = audience, SigningKey = new SymmetricSecurityKey(key) };
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = ClockSkew,
        NameClaimType = "sub"
    };
}
