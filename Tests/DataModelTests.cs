using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TenNet.Api;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Tests;

public sealed class DataModelTests
{
    [Fact]
    public async Task MigrationAndSeederCreateExpectedRowsAndPreserveEditsOnRestart()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();
        var password = Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Seed:Passwords:alice"] = password,
                ["Seed:Passwords:bob"] = password,
                ["Seed:Passwords:carol"] = password
            }).Build();

        await DbSeeder.SeedAsync(db, configuration, new TestEnvironment());
        var alice = await db.AppUsers.SingleAsync(x => x.UserName == "alice");
        var hash = alice.PasswordHash;
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<AppUser>().VerifyHashedPassword(alice, hash, password));
        var item = await db.InventoryItems.FirstAsync();
        item.Quantity = 999;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await DbSeeder.SeedAsync(db, new ConfigurationBuilder().Build(), new TestEnvironment());

        Assert.Equal(3, await db.Tenants.CountAsync());
        Assert.Equal(3, await db.AppUsers.CountAsync());
        Assert.Equal(4, await db.UserTenantMemberships.CountAsync());
        Assert.Equal(18, await db.InventoryItems.CountAsync());
        Assert.All(await db.InventoryItems.GroupBy(x => x.TenantId).Select(x => x.Count()).ToListAsync(),
            count => Assert.Equal(6, count));
        Assert.Equal(999, (await db.InventoryItems.SingleAsync(x => x.Id == item.Id)).Quantity);
        Assert.Equal(hash, (await db.AppUsers.SingleAsync(x => x.Id == alice.Id)).PasswordHash);
        var membershipSlugs = await db.UserTenantMemberships.Where(x => x.UserId == alice.Id)
            .Select(x => x.Tenant.Slug).OrderBy(x => x).ToListAsync();
        Assert.Equal(new[] { "acme", "globex" }, membershipSlugs);
    }

    [Fact]
    public async Task StaleUpdateFailsAndRowVersionChanges()
    {
        await using var connection = await OpenConnectionAsync();
        await using var first = CreateContext(connection);
        await first.Database.MigrateAsync();
        var item = NewItem(new Tenant { Name = "Test", Slug = "test" });
        first.Add(item);
        await first.SaveChangesAsync();
        Assert.NotEqual(Guid.Empty, item.RowVersion);
        var oldVersion = item.RowVersion;
        await using var second = CreateContext(connection);
        var stale = await second.InventoryItems.SingleAsync();
        item.Quantity = 2;
        first.SaveChanges();
        Assert.NotEqual(oldVersion, item.RowVersion);
        stale.Quantity = 3;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task CrossTenantFileForeignKeyIsRejected()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();
        var item = NewItem(new Tenant { Name = "A", Slug = "a" });
        var other = new Tenant { Name = "B", Slug = "b" };
        db.AddRange(item, other);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.ItemFiles.Add(new ItemFile
        {
            ItemId = item.Id, TenantId = other.Id, OriginalName = "report.csv",
            StoredKey = "test-only-key", ContentType = "text/csv", Size = 10
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateSkuWithinTenantAndDeletingReferencedTenantAreRejected()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();
        var tenant = new Tenant { Name = "A", Slug = "a" };
        db.Add(NewItem(tenant));
        db.Add(NewItem(new Tenant { Name = "B", Slug = "b" }));
        await db.SaveChangesAsync();
        db.Add(NewItem(tenant));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Remove(new Tenant { Id = tenant.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateEmailIsRejected()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();
        db.AppUsers.Add(new AppUser { UserName = "one", Email = "same@example.test", PasswordHash = "test-only" });
        await db.SaveChangesAsync();
        db.AppUsers.Add(new AppUser { UserName = "two", Email = "same@example.test", PasswordHash = "test-only" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SeederRefusesProduction()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DbSeeder.SeedAsync(db,
            new ConfigurationBuilder().Build(), new TestEnvironment { EnvironmentName = Environments.Production }));
    }

    private static InventoryItem NewItem(Tenant tenant) =>
        new() { Tenant = tenant, Name = "Test item", Sku = "TEST-001", Quantity = 1, Price = 9.99m };

    private static AppDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        return connection;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TenNet.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
