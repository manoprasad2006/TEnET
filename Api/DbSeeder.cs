using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration configuration,
        IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Demo seeding is restricted to Development.");

        db.IsSeedingMode = true;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var tenants = new Dictionary<string, Tenant>();
            foreach (var name in new[] { "Acme", "Globex", "Initech" })
            {
                var slug = name.ToLowerInvariant();
                var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
                if (tenant is null)
                {
                    tenant = new Tenant { Name = name, Slug = slug };
                    db.Tenants.Add(tenant);
                }
                tenants.Add(slug, tenant);
            }

            var memberships = new Dictionary<string, string[]>
            {
                ["alice"] = ["acme", "globex"],
                ["bob"] = ["initech"],
                ["carol"] = ["acme"]
            };
            var hasher = new PasswordHasher<AppUser>();
            foreach (var (name, slugs) in memberships)
            {
                var email = $"{name}@tennet.example";
                var user = await db.AppUsers.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
                if (user is null)
                {
                    var password = configuration[$"Seed:Passwords:{name}"];
                    if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
                        throw new InvalidOperationException($"Set Seed:Passwords:{name} using user-secrets (see README).");
                    user = new AppUser { UserName = name, Email = email };
                    user.PasswordHash = hasher.HashPassword(user, password);
                    db.AppUsers.Add(user);
                }

                foreach (var slug in slugs)
                {
                    var tenant = tenants[slug];
                    if (!await db.UserTenantMemberships.AnyAsync(
                        x => x.UserId == user.Id && x.TenantId == tenant.Id, cancellationToken))
                        db.UserTenantMemberships.Add(new UserTenantMembership { User = user, Tenant = tenant });
                }
            }

            (string Slug, string Sku, string Name, int Quantity, decimal Price)[] catalog =
            [
                ("acme", "ACM-DRILL-001", "Cordless drill", 24, 89.99m),
                ("acme", "ACM-BOLT-002", "M8 bolt pack", 150, 6.50m),
                ("acme", "ACM-GLOVE-003", "Work gloves", 80, 12.00m),
                ("acme", "ACM-TAPE-004", "Measuring tape", 45, 9.75m),
                ("acme", "ACM-HELMET-005", "Safety helmet", 30, 22.50m),
                ("acme", "ACM-LEVEL-006", "Spirit level", 18, 19.95m),
                ("globex", "GLX-SENSOR-001", "Temperature sensor", 60, 34.90m),
                ("globex", "GLX-RELAY-002", "Industrial relay", 120, 8.25m),
                ("globex", "GLX-CABLE-003", "Ethernet cable 5m", 200, 7.99m),
                ("globex", "GLX-SWITCH-004", "Network switch", 15, 119.00m),
                ("globex", "GLX-ADAPTER-005", "Power adapter", 55, 18.50m),
                ("globex", "GLX-METER-006", "Digital multimeter", 22, 49.95m),
                ("initech", "INT-KEYBOARD-001", "USB keyboard", 70, 29.99m),
                ("initech", "INT-MOUSE-002", "Wireless mouse", 85, 24.50m),
                ("initech", "INT-MONITOR-003", "24 inch monitor", 25, 179.00m),
                ("initech", "INT-DOCK-004", "USB-C docking station", 35, 94.95m),
                ("initech", "INT-HEADSET-005", "Office headset", 50, 45.00m),
                ("initech", "INT-STAND-006", "Laptop stand", 40, 32.75m)
            ];

            foreach (var (slug, sku, name, quantity, price) in catalog)
            {
                var tenant = tenants[slug];
                if (!await db.InventoryItems.AnyAsync(x => x.TenantId == tenant.Id && x.Sku == sku, cancellationToken))
                    db.InventoryItems.Add(new InventoryItem
                    {
                        Tenant = tenant, Name = name, Sku = sku, Quantity = quantity, Price = price
                    });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            db.IsSeedingMode = false;
        }
    }
}
