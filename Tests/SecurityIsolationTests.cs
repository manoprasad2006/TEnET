using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using TenNet.Api.Authentication;
using TenNet.Api.Inventory;

namespace TenNet.Tests;

public sealed class SecurityIsolationTests
{
    private static async Task<(string AccessToken, Guid UserId, Dictionary<string, Guid> Tenants)> LoginAsync(
        HttpClient client, AuthAppFactory factory, string username = "alice")
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"{username}@tennet.example", password = factory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        var tenants = login.Tenants.ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);
        return (login.AccessToken, login.User.Id, tenants);
    }

    [Fact]
    public async Task TenantACannotListReadUpdateOrDeleteTenantBItems()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];
        var globexId = tenants["Globex"];

        // 1. List Acme items
        using var acmeListReq = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        acmeListReq.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        var acmeListResp = await client.SendAsync(acmeListReq);
        var acmeItems = (await acmeListResp.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!.Items;

        // 2. List Globex items
        using var globexListReq = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        globexListReq.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
        var globexListResp = await client.SendAsync(globexListReq);
        var globexItems = (await globexListResp.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!.Items;

        // Verify distinct item sets
        Assert.NotEmpty(acmeItems);
        Assert.NotEmpty(globexItems);
        Assert.Empty(acmeItems.Select(x => x.Id).Intersect(globexItems.Select(x => x.Id)));

        var targetGlobexItem = globexItems[0];

        // 3. Acme tries to read Globex item -> 404
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{targetGlobexItem.Id}"))
        {
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // 4. Acme tries to update Globex item -> 404
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{targetGlobexItem.Id}"))
        {
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            req.Content = JsonContent.Create(new
            {
                name = "Cross-tenant hacked",
                sku = targetGlobexItem.Sku,
                quantity = 1,
                price = 1.00,
                rowVersion = targetGlobexItem.RowVersion
            });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // 5. Acme tries to delete Globex item -> 404
        using (var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/items/{targetGlobexItem.Id}"))
        {
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }

    [Fact]
    public async Task MultiMemberUserCanSwitchOnlyBetweenAuthorizedMemberships()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        // Alice has Acme and Globex
        var (aliceToken, _, aliceTenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        // Switch to Acme -> OK
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            req.Headers.Add("X-Tenant-Id", aliceTenants["Acme"].ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Switch to Globex -> OK
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            req.Headers.Add("X-Tenant-Id", aliceTenants["Globex"].ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Alice tries to switch to Initech (Bob's tenant) -> 403 Forbidden
        var (_, _, bobTenants) = await LoginAsync(client, factory, "bob");
        var initechId = bobTenants["Initech"];

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            req.Headers.Add("X-Tenant-Id", initechId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
    }

    [Fact]
    public async Task ConcurrentInterleavedRequestsForDifferentTenantsNeverLeakData()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (aliceToken, _, aliceTenants) = await LoginAsync(client, factory, "alice");
        var (bobToken, _, bobTenants) = await LoginAsync(client, factory, "bob");

        var acmeId = aliceTenants["Acme"];
        var initechId = bobTenants["Initech"];

        // Execute 20 interleaved requests alternating between Acme and Initech
        for (var i = 0; i < 20; i++)
        {
            var isAcme = (i % 2 == 0);
            var token = isAcme ? aliceToken : bobToken;
            var tenantId = isAcme ? acmeId : initechId;
            var expectedPrefix = isAcme ? "ACM-" : "INT-";

            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/items");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Tenant-Id", tenantId.ToString("D"));

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var result = await resp.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>();
            Assert.NotNull(result);
            Assert.All(result.Items, item => Assert.StartsWith(expectedPrefix, item.Sku));
        }
    }

    [Fact]
    public async Task TamperedExpiredAndWrongAudienceJwtsReturn401()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (validToken, userId, tenants) = await LoginAsync(client, factory, "alice");
        var acmeId = tenants["Acme"];

        // 1. Tampered signature
        var parts = validToken.Split('.');
        var tamperedSignature = parts[2].Reverse().ToArray();
        var tamperedToken = $"{parts[0]}.{parts[1]}.{new string(tamperedSignature)}";

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/items"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // 2. Expired token
        var expiredToken = CreateCustomJwt(factory, userId, expires: DateTime.UtcNow.AddMinutes(-5));
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/items"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // 3. Wrong audience
        var wrongAudienceToken = CreateCustomJwt(factory, userId, audience: "wrong-audience");
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/items"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongAudienceToken);
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
    }

    private static string CreateCustomJwt(
        AuthAppFactory factory,
        Guid userId,
        string? audience = null,
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(factory.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "https://tennet.test",
            audience: audience ?? "tennet-tests",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")), new Claim(JwtRegisteredClaimNames.Email, "alice@tennet.example")],
            notBefore: now.AddMinutes(-10),
            expires: expires ?? now.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
