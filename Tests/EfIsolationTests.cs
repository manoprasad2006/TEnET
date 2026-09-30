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

public sealed class EfIsolationTests
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
    public async Task EmptyTenantContextReturnsZeroRowsAndRejectsWrites()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var emptyContext = new TenantContext(); // Unset
        var db = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            emptyContext);

        // Reads match 0 rows
        var items = await db.InventoryItems.ToListAsync();
        Assert.Empty(items);

        var files = await db.ItemFiles.ToListAsync();
        Assert.Empty(files);

        var audits = await db.AuditLogs.ToListAsync();
        Assert.Empty(audits);

        // Writes fail closed with TenantViolationException
        db.InventoryItems.Add(new InventoryItem
        {
            Name = "Forbidden item",
            Sku = "FORBIDDEN-001",
            Quantity = 10,
            Price = 19.99m
        });

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TenantACannotSeeTenantBItems()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<TenantLookupContext>();
        var acme = await lookup.Tenants.SingleAsync(t => t.Slug == "acme");
        var globex = await lookup.Tenants.SingleAsync(t => t.Slug == "globex");

        var acmeContext = new TenantContext();
        acmeContext.SetTenant(acme.Id, acme.Name);
        var acmeDb = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            acmeContext);

        var globexContext = new TenantContext();
        globexContext.SetTenant(globex.Id, globex.Name);
        var globexDb = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            globexContext);

        // Acme sees only Acme's items
        var acmeItems = await acmeDb.InventoryItems.ToListAsync();
        Assert.Equal(6, acmeItems.Count);
        Assert.All(acmeItems, item => Assert.Equal(acme.Id, item.TenantId));

        // Globex sees only Globex's items
        var globexItems = await globexDb.InventoryItems.ToListAsync();
        Assert.Equal(6, globexItems.Count);
        Assert.All(globexItems, item => Assert.Equal(globex.Id, item.TenantId));

        // No item overlap
        var acmeIds = acmeItems.Select(x => x.Id).ToHashSet();
        var globexIds = globexItems.Select(x => x.Id).ToHashSet();
        Assert.Empty(acmeIds.Intersect(globexIds));
    }

    [Fact]
    public async Task SaveChangesStampsTenantIdIgnoringClientValue()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<TenantLookupContext>();
        var acme = await lookup.Tenants.SingleAsync(t => t.Slug == "acme");

        var spoofedTenantId = Guid.NewGuid();

        var acmeContext = new TenantContext();
        acmeContext.SetTenant(acme.Id, acme.Name);
        var db = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            acmeContext);

        var newItem = new InventoryItem
        {
            TenantId = spoofedTenantId, // Client attempts to spoof TenantId
            Name = "Safety goggles",
            Sku = "ACM-GOGGLE-001",
            Quantity = 50,
            Price = 14.50m
        };

        db.InventoryItems.Add(newItem);
        await db.SaveChangesAsync();

        // Stamped with acme.Id, ignoring client spoofed value
        Assert.Equal(acme.Id, newItem.TenantId);
        Assert.NotEqual(spoofedTenantId, newItem.TenantId);
    }

    [Fact]
    public async Task ChangingTenantIdThrowsTenantViolationException()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<TenantLookupContext>();
        var acme = await lookup.Tenants.SingleAsync(t => t.Slug == "acme");

        var acmeContext = new TenantContext();
        acmeContext.SetTenant(acme.Id, acme.Name);
        var db = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            acmeContext);

        var item = await db.InventoryItems.FirstAsync();
        item.TenantId = Guid.NewGuid(); // Attempt to reassign tenant

        var ex = await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossTenantModifyOrDeleteThrowsTenantViolationException()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<TenantLookupContext>();
        var acme = await lookup.Tenants.SingleAsync(t => t.Slug == "acme");
        var globex = await lookup.Tenants.SingleAsync(t => t.Slug == "globex");

        // Load an item belonging to Globex
        var globexContext = new TenantContext();
        globexContext.SetTenant(globex.Id, globex.Name);
        var globexDb = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            globexContext);
        var globexItem = await globexDb.InventoryItems.FirstAsync();

        // Try to update it in Acme context
        var acmeContext = new TenantContext();
        acmeContext.SetTenant(acme.Id, acme.Name);
        var acmeDb = new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            acmeContext);

        acmeDb.Attach(globexItem);
        globexItem.Name = "Hacked Globex item";
        acmeDb.Entry(globexItem).State = EntityState.Modified;

        var ex = await Assert.ThrowsAsync<TenantViolationException>(() => acmeDb.SaveChangesAsync());
        Assert.Contains("Cross-tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerificationEndpointReturnsTenantSpecificCounts()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Check Acme counts
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/counts"))
        {
            request.Headers.Add("X-Tenant-Id", tenants["Acme"].ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(6, body.GetProperty("items").GetInt32());
            Assert.Equal(0, body.GetProperty("files").GetInt32());
            Assert.Equal(0, body.GetProperty("audits").GetInt32());
        }

        // Check Globex counts
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/counts"))
        {
            request.Headers.Add("X-Tenant-Id", tenants["Globex"].ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(6, body.GetProperty("items").GetInt32());
        }
    }
}
