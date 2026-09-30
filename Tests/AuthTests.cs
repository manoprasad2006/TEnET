using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TenNet.Api.Authentication;
using TenNet.Data;

namespace TenNet.Tests;

public sealed class AuthTests
{
    [Theory]
    [InlineData("alice", 2)]
    [InlineData("bob", 1)]
    [InlineData("carol", 1)]
    public async Task LoginReturnsShortLivedTokenAndOnlyUsersMemberships(string name, int count)
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"{name.ToUpperInvariant()}@tennet.example", password = factory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(count, login.Tenants.Count);
        Assert.Equal(name, login.User.UserName);
        var expected = name == "alice" ? new[] { "Acme", "Globex" } : name == "bob" ? ["Initech"] : ["Acme"];
        Assert.Equal(expected, login.Tenants.Select(x => x.Name));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal(login.User.Id.ToString("D"), token.Subject);
        Assert.Equal(900, (token.ValidTo - token.ValidFrom).TotalSeconds);
        Assert.All(token.Claims, claim => Assert.Contains(claim.Type, new[] { "sub", "email", "nbf", "exp", "iss", "aud" }));
        Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(login.User.Id, me!.User.Id);
        Assert.Equal(expected, me.Tenants.Select(x => x.Name));
    }

    [Fact]
    public async Task MeReadsCurrentMembershipsInsteadOfTokenPermissions()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await db.UserTenantMemberships.SingleAsync(x => x.UserId == login.User.Id && x.Tenant.Slug == "globex");
            db.Remove(membership);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal("Acme", Assert.Single(me!.Tenants).Name);
    }

    [Fact]
    public async Task UnknownEmailAndWrongPasswordUseSameErrorShapeAndMinimumDelay()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        JsonElement? previous = null;
        foreach (var email in new[] { "alice@tennet.example", "unknown@tennet.example" })
        {
            var started = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "incorrect" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(450));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Invalid credentials", body.GetProperty("title").GetString());
            if (previous is { } last)
            {
                Assert.Equal(last.EnumerateObject().Select(x => x.Name), body.EnumerateObject().Select(x => x.Name));
                Assert.Equal(last.GetProperty("type").GetString(), body.GetProperty("type").GetString());
            }
            previous = body;
        }
    }

    [Fact]
    public async Task MeWithoutTokenIs401AndHealthIsAnonymous()
    {
        using var factory = new AuthAppFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/hello")).StatusCode);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("future")]
    [InlineData("unsigned")]
    public async Task InvalidTokensAreRejected(string defect)
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        Guid userId;
        using (var scope = factory.Services.CreateScope())
            userId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppUsers
                .Where(x => x.UserName == "alice").Select(x => x.Id).SingleAsync();
        var now = DateTime.UtcNow;
        var key = defect == "signature" ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(factory.SigningKey);
        var jwt = new JwtSecurityToken(
            defect == "issuer" ? "https://wrong.test" : "https://tennet.test",
            defect == "audience" ? "wrong-audience" : "tennet-tests",
            [new Claim("sub", userId.ToString("D")), new Claim("email", "alice@tennet.example")],
            notBefore: defect == "future" ? now.AddMinutes(5) : now.AddMinutes(-20),
            expires: defect == "expired" ? now.AddMinutes(-2) : now.AddMinutes(15),
            signingCredentials: defect == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (defect == "tampered")
        {
            var index = token.LastIndexOf('.') + 1;
            token = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"invalid\",\"password\":\"x\"}")]
    [InlineData("{\"email\":\"alice@tennet.example\",\"password\":\"x\",\"tenantId\":\"unexpected\"}")]
    public async Task InvalidLoginDtoIs400(string json)
    {
        using var factory = new AuthAppFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/auth/login", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginIsLimitedToFiveAttemptsAndForwardedHeaderCannotBypassIt()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "unknown@tennet.example", password = "incorrect" });
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
            if (attempt == 5) Assert.NotNull(response.Headers.RetryAfter);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-base64")]
    [InlineData("YWJj")]
    public void MissingMalformedOrWeakSigningKeyIsRejected(string key)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://tennet.test", ["Jwt:Audience"] = "tennet-tests", ["Jwt:SigningKey"] = key
        }).Build();
        Assert.Throws<InvalidOperationException>(() => JwtSettings.Load(config));
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, AuthAppFactory factory)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "alice@tennet.example", password = factory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
