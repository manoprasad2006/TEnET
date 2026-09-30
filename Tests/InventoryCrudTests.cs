using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenNet.Api.Authentication;
using TenNet.Api.Inventory;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Tests;

public sealed class InventoryCrudTests
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
    public async Task ListItemsReturnsPaginatedBoundedResultsForCurrentTenantOnly()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var acmeId = tenants["Acme"];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?page=1&pageSize=4");
        request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task SearchAndSortItemsOperatesSafely()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var acmeId = tenants["Acme"];
        // Search for drill
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?search=drill"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
            Assert.Single(result.Items);
            Assert.Contains("drill", result.Items[0].Name, StringComparison.OrdinalIgnoreCase);
        }

        // Sort descending by price
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/items?sortBy=price&sortDescending=true"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
            Assert.True(result.Items[0].Price >= result.Items[1].Price);
        }
    }

    [Fact]
    public async Task CreateItemEnforcesValidationAndCreatesAuditLog()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, userId, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];

        // 1. Invalid SKU regex
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/items"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            request.Content = JsonContent.Create(new { name = "Invalid SKU Item", sku = "SKU with spaces!", quantity = 10, price = 25.00 });
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // 2. Valid creation
        ItemResponse createdItem;
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/items"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            request.Content = JsonContent.Create(new { name = "Industrial Hammer", sku = "ACM-HAMMER-001", quantity = 35, price = 29.99 });
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            createdItem = (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
            Assert.Equal("Industrial Hammer", createdItem.Name);
            Assert.Equal("ACM-HAMMER-001", createdItem.Sku);
            Assert.Equal(35, createdItem.Quantity);
            Assert.Equal(29.99m, createdItem.Price);
            Assert.NotEqual(Guid.Empty, createdItem.RowVersion);
        }

        // 3. Verify audit log created
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BypassTenantFilter = true;
            var audit = await db.AuditLogs.SingleOrDefaultAsync(x => x.EntityId == createdItem.Id && x.Action == "CREATE");
            Assert.NotNull(audit);
            Assert.Equal(acmeId, audit.TenantId);
            Assert.Equal(userId, audit.UserId);
        }
    }

    [Fact]
    public async Task DuplicateSkuInSameTenantReturns409Conflict()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];

        // Existing SKU ACM-DRILL-001 in Acme
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        request.Content = JsonContent.Create(new { name = "Duplicate Drill", sku = "ACM-DRILL-001", quantity = 10, price = 50.00 });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Conflict", body.GetProperty("title").GetString());
        Assert.Contains("ACM-DRILL-001", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task SameSkuInDifferentTenantIsAllowed()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var globexId = tenants["Globex"];

        // Acme has ACM-DRILL-001. Now add ACM-DRILL-001 to Globex
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
        request.Content = JsonContent.Create(new { name = "Globex Drill", sku = "ACM-DRILL-001", quantity = 10, price = 50.00 });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CrossTenantAndNonExistentItemIdsReturnIdentical404()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];
        var globexId = tenants["Globex"];

        // Get an item belonging to Globex
        ItemResponse globexItem;
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/items"))
        {
            request.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
            var response = await client.SendAsync(request);
            var result = (await response.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
            globexItem = result.Items[0];
        }

        // 1. Acme requests Globex's item ID -> 404
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{globexItem.Id}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Item not found", body.GetProperty("title").GetString());
        }

        // 2. Acme requests completely non-existent ID -> 404
        var nonExistentId = Guid.NewGuid();
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{nonExistentId}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Item not found", body.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task UpdateItemRequiresRowVersionAndRejectsConcurrencyConflicts()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];

        // Get an item
        ItemResponse item;
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/items"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            var result = (await response.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
            item = result.Items[0];
        }

        // 1. Conflict when wrong RowVersion is provided
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{item.Id}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            request.Content = JsonContent.Create(new
            {
                name = "Updated Name",
                sku = item.Sku,
                quantity = 50,
                price = item.Price,
                rowVersion = Guid.NewGuid() // Stale / incorrect RowVersion
            });
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // 2. Successful update with matching RowVersion
        ItemResponse updated;
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{item.Id}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            request.Content = JsonContent.Create(new
            {
                name = "Updated Drill Pro",
                sku = item.Sku,
                quantity = 88,
                price = item.Price,
                rowVersion = item.RowVersion
            });
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            updated = (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
            Assert.Equal("Updated Drill Pro", updated.Name);
            Assert.Equal(88, updated.Quantity);
            Assert.NotEqual(item.RowVersion, updated.RowVersion); // RowVersion refreshed
        }
    }

    [Fact]
    public async Task DeleteItemRemovesItemAndWritesAudit()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, userId, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];

        // Create an item to delete
        ItemResponse created;
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/items"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            request.Content = JsonContent.Create(new { name = "Item to delete", sku = "DEL-001", quantity = 1, price = 5.00 });
            var response = await client.SendAsync(request);
            created = (await response.Content.ReadFromJsonAsync<ItemResponse>())!;
        }

        // Delete the item
        using (var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/items/{created.Id}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Subsequent get returns 404
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{created.Id}"))
        {
            request.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Audit log exists
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BypassTenantFilter = true;
            var deleteAudit = await db.AuditLogs.SingleOrDefaultAsync(x => x.EntityId == created.Id && x.Action == "DELETE");
            Assert.NotNull(deleteAudit);
            Assert.Equal(userId, deleteAudit.UserId);
        }
    }
}
