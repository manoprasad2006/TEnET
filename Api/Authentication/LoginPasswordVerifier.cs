using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using TenNet.Domain;

namespace TenNet.Api.Authentication;

public sealed class LoginPasswordVerifier
{
    private readonly PasswordHasher<AppUser> hasher = new();
    private readonly AppUser dummyUser = new();
    private readonly string dummyHash;

    public LoginPasswordVerifier()
    {
        dummyHash = hasher.HashPassword(dummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public bool Verify(AppUser? user, string password)
    {
        // Unknown users still incur the same PBKDF2 work as the seeded users.
        var result = hasher.VerifyHashedPassword(user ?? dummyUser, user?.PasswordHash ?? dummyHash, password);
        return user is not null && result != PasswordVerificationResult.Failed;
    }
}
