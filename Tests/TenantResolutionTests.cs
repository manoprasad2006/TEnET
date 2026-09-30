using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenNet.Api.Authentication;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Tests;

public sealed class TenantResolutionTests
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
    public void TenantContextRejectsSettingTwice()
    {
        var context = new TenantContext();
        Assert.False(context.IsSet);
        Assert.Null(context.TenantId);
        Assert.Null(context.TenantName);

        var id1 = Guid.NewGuid();
        context.SetTenant(id1, "Acme");
        Assert.True(context.IsSet);
        Assert.Equal(id1, context.TenantId);
        Assert.Equal("Acme", context.TenantName);

        var ex = Assert.Throws<InvalidOperationException>(() => context.SetTenant(Guid.NewGuid(), "Globex"));
        Assert.Contains("already been set", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TenantContextRejectsEmptyGuidOrNullName()
    {
        var context = new TenantContext();
        Assert.Throws<ArgumentException>(() => context.SetTenant(Guid.Empty, "Acme"));
        Assert.Throws<ArgumentException>(() => context.SetTenant(Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => context.SetTenant(Guid.NewGuid(), "   "));
    }

    [Fact]
    public async Task ValidTenantSelectionReturnsWhoAmI()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, userId, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Alice accessing Acme
        var acmeId = tenants["Acme"];
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(acmeId, body.GetProperty("tenantId").GetGuid());
            Assert.Equal("Acme", body.GetProperty("tenantName").GetString());
            Assert.Equal(userId, body.GetProperty("userId").GetGuid());
        }

        // Alice accessing Globex
        var globexId = tenants["Globex"];
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            request.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(globexId, body.GetProperty("tenantId").GetGuid());
            Assert.Equal("Globex", body.GetProperty("tenantName").GetString());
            Assert.Equal(userId, body.GetProperty("userId").GetGuid());
        }
    }

    [Fact]
    public async Task MissingTenantHeaderReturns400()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, _) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/tenant/whoami");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Contains("Missing", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{550e8400-e29b-41d4-a716-446655440000}")]
    [InlineData("550e8400e29b41d4a716446655440000")]
    public async Task MalformedTenantHeaderReturns400(string header)
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, _) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
        request.Headers.Add("X-Tenant-Id", header);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Contains("Malformed or invalid", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task DuplicateTenantHeaderReturns400()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Test with multiple header entries
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            request.Headers.Add("X-Tenant-Id", [tenants["Acme"].ToString("D"), tenants["Globex"].ToString("D")]);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Bad Request", body.GetProperty("title").GetString());
            Assert.Contains("Duplicate", body.GetProperty("detail").GetString());
        }

        // Test with comma-separated concatenation
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami"))
        {
            request.Headers.Add("X-Tenant-Id", $"{tenants["Acme"]:D},{tenants["Globex"]:D}");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Bad Request", body.GetProperty("title").GetString());
            Assert.Contains("Duplicate", body.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task OversizedTenantHeaderReturns400()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, _) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var oversized = new string('a', 128);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
        request.Headers.Add("X-Tenant-Id", oversized);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Contains("exceeds maximum allowed length", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("1; DROP TABLE Tenants; --")]
    [InlineData("'; SELECT * FROM AppUsers; --")]
    [InlineData("UNION SELECT 1, 'test' --")]
    public async Task SqlInjectionStyleTenantHeaderReturns400(string injectionPayload)
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, _) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
        request.Headers.Add("X-Tenant-Id", injectionPayload);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Contains("Malformed or invalid", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnknownTenantIdReturns404()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, _) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var randomTenantId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
        request.Headers.Add("X-Tenant-Id", randomTenantId.ToString("D"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tenant not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnauthorizedTenantSelectionReturns403()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        // Get Acme tenant ID using alice
        var (_, _, aliceTenants) = await LoginAsync(client, factory, "alice");
        var acmeId = aliceTenants["Acme"];

        // Login as Bob (member of Initech only)
        var (bobToken, _, bobTenants) = await LoginAsync(client, factory, "bob");
        Assert.DoesNotContain("Acme", bobTenants.Keys);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        // Bob tries to select Acme
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
        request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Forbidden", body.GetProperty("title").GetString());
        Assert.Contains("not a member", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnauthenticatedTenantRequestReturns401()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantLookupContext>();
            var acme = await db.Tenants.SingleAsync(t => t.Slug == "acme");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/whoami");
            request.Headers.Add("X-Tenant-Id", acme.Id.ToString("D"));
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousAndExemptEndpointsBypassTenantResolution()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        // 1. /hello (anonymous) without tenant header
        var hello = await client.GetAsync("/hello");
        Assert.Equal(HttpStatusCode.OK, hello.StatusCode);

        // 2. /health (anonymous) without tenant header
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // 3. /api/auth/login (anonymous) without tenant header
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "alice@tennet.example", password = factory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginResponse = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        // 4. /api/auth/me (authenticated, but exempt from tenant header)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse.AccessToken);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task SubdomainTenantResolverWhenDisabledReturnsMissing()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TenNet.Api.Tenancy.SubdomainOptions { Enabled = false });
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TenNet.Api.Tenancy.SubdomainTenantResolver>.Instance;
        var resolver = new TenNet.Api.Tenancy.SubdomainTenantResolver(options, logger);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("acme.tennet.example");

        var result = await resolver.ResolveAsync(httpContext);
        Assert.Equal(TenNet.Api.Tenancy.TenantResolutionStatus.Missing, result.Status);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompositeResolverEvaluatesInOrderAndFailsClosedOnInvalid()
    {
        var validId = Guid.NewGuid();
        var resolverMissing = new StubResolver(TenNet.Api.Tenancy.TenantResolutionResult.Missing("Missing from first"));
        var resolverInvalid = new StubResolver(TenNet.Api.Tenancy.TenantResolutionResult.Invalid("Malformed from second"));
        var resolverSuccess = new StubResolver(TenNet.Api.Tenancy.TenantResolutionResult.Success(validId, validId.ToString()));

        // First resolver is missing, second is invalid -> fails closed with invalid (never reaches third)
        var composite1 = new TenNet.Api.Tenancy.CompositeTenantResolver([resolverMissing, resolverInvalid, resolverSuccess]);
        var result1 = await composite1.ResolveAsync(new Microsoft.AspNetCore.Http.DefaultHttpContext());
        Assert.Equal(TenNet.Api.Tenancy.TenantResolutionStatus.Invalid, result1.Status);
        Assert.Equal("Malformed from second", result1.ErrorMessage);

        // First resolver is missing, second is success -> returns success
        var composite2 = new TenNet.Api.Tenancy.CompositeTenantResolver([resolverMissing, resolverSuccess]);
        var result2 = await composite2.ResolveAsync(new Microsoft.AspNetCore.Http.DefaultHttpContext());
        Assert.Equal(TenNet.Api.Tenancy.TenantResolutionStatus.Success, result2.Status);
        Assert.Equal(validId, result2.TenantId);
    }

    private sealed class StubResolver(TenNet.Api.Tenancy.TenantResolutionResult result) : TenNet.Api.Tenancy.ITenantResolver
    {
        public Task<TenNet.Api.Tenancy.TenantResolutionResult> ResolveAsync(Microsoft.AspNetCore.Http.HttpContext context) =>
            Task.FromResult(result);
    }
}

