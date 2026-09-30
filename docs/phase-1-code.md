# Phase 1 — complete source handoff

This is the verified Phase 1 snapshot. Future edits belong in the original files.

The complete updated runbook and demo credentials are in [README.md](D:/Desktop/TenNet/README.md).
Credentials remain documented only there. The database and build output are ignored generated files.

## Files created or changed

- [.config/dotnet-tools.json](D:/Desktop/TenNet/.config/dotnet-tools.json)
- [.gitignore](D:/Desktop/TenNet/.gitignore)
- [Domain/Entities.cs](D:/Desktop/TenNet/Domain/Entities.cs)
- [Data/TenNet.Data.csproj](D:/Desktop/TenNet/Data/TenNet.Data.csproj)
- [Data/AppDbContext.cs](D:/Desktop/TenNet/Data/AppDbContext.cs)
- [Data/Migrations/20260930075313_InitialCreate.cs](D:/Desktop/TenNet/Data/Migrations/20260930075313_InitialCreate.cs)
- [Data/Migrations/20260930075313_InitialCreate.Designer.cs](D:/Desktop/TenNet/Data/Migrations/20260930075313_InitialCreate.Designer.cs)
- [Data/Migrations/AppDbContextModelSnapshot.cs](D:/Desktop/TenNet/Data/Migrations/AppDbContextModelSnapshot.cs)
- [Api/TenNet.Api.csproj](D:/Desktop/TenNet/Api/TenNet.Api.csproj)
- [Api/Program.cs](D:/Desktop/TenNet/Api/Program.cs)
- [Api/DbSeeder.cs](D:/Desktop/TenNet/Api/DbSeeder.cs)
- [Api/appsettings.json](D:/Desktop/TenNet/Api/appsettings.json)
- [Tests/TenNet.Tests.csproj](D:/Desktop/TenNet/Tests/TenNet.Tests.csproj)
- [Tests/DataModelTests.cs](D:/Desktop/TenNet/Tests/DataModelTests.cs)
- [Tests/README.md](D:/Desktop/TenNet/Tests/README.md)
- [README.md](D:/Desktop/TenNet/README.md) — full runbook, verification and residual risks.
- [docs/phase-1-code.md](D:/Desktop/TenNet/docs/phase-1-code.md) — this generated source handoff.

External development user-secrets were configured; their values are not reproduced here.

## .config/dotnet-tools.json

````json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "8.0.31",
      "commands": [
        "dotnet-ef"
      ],
      "rollForward": false
    }
  }
}
````

## .gitignore

````text
**/bin/
**/obj/
.vs/
.idea/
TestResults/
coverage/
**/node_modules/
**/dist/
.env
.env.*
!.env.example
**/secrets.json
**/secrets.*.json
**/appsettings.Local.json
**/appsettings.*.local.json
*.db
*.db-*
*.sqlite
*.sqlite-*
*.sqlite3
*.sqlite3-*
*.pfx
*.pem
*.key
*.log
````

## Domain/Entities.cs

````csharp
namespace TenNet.Domain;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public sealed class UserTenantMembership
{
    // The membership has the explicitly requested composite primary key.
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public AppUser User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public sealed class InventoryItem : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid RowVersion { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public ICollection<ItemFile> Files { get; set; } = new List<ItemFile>();
}

public sealed class ItemFile : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public Guid TenantId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string StoredKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public InventoryItem Item { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public sealed class AuditLog : ITenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
````

## Data/TenNet.Data.csproj

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.31" />
    <ProjectReference Include="..\Domain\TenNet.Domain.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
````

## Data/AppDbContext.cs

````csharp
using Microsoft.EntityFrameworkCore;
using TenNet.Domain;

namespace TenNet.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserTenantMembership> UserTenantMemberships => Set<UserTenantMembership>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<ItemFile> ItemFiles => Set<ItemFile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenants = modelBuilder.Entity<Tenant>();
        tenants.HasKey(x => x.Id);
        tenants.Property(x => x.Name).HasMaxLength(100).IsRequired();
        tenants.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        tenants.HasIndex(x => x.Slug).IsUnique();

        var users = modelBuilder.Entity<AppUser>();
        users.HasKey(x => x.Id);
        users.Property(x => x.UserName).HasMaxLength(64).IsRequired();
        users.Property(x => x.Email).HasMaxLength(254).IsRequired();
        users.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        users.HasIndex(x => x.Email).IsUnique();
        users.HasIndex(x => x.UserName).IsUnique();

        var memberships = modelBuilder.Entity<UserTenantMembership>();
        memberships.HasKey(x => new { x.UserId, x.TenantId });
        memberships.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        memberships.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);

        var items = modelBuilder.Entity<InventoryItem>();
        items.HasKey(x => x.Id);
        items.HasAlternateKey(x => new { x.TenantId, x.Id });
        items.Property(x => x.Name).HasMaxLength(200).IsRequired();
        items.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        items.Property(x => x.Price).HasPrecision(18, 2);
        items.Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        items.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        items.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);

        var files = modelBuilder.Entity<ItemFile>();
        files.HasKey(x => x.Id);
        files.Property(x => x.OriginalName).HasMaxLength(255).IsRequired();
        files.Property(x => x.StoredKey).HasMaxLength(512).IsRequired();
        files.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        files.HasIndex(x => x.StoredKey).IsUnique();
        files.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        files.HasOne(x => x.Item).WithMany(x => x.Files)
            .HasForeignKey(x => new { x.TenantId, x.ItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id });

        var audits = modelBuilder.Entity<AuditLog>();
        audits.HasKey(x => x.Id);
        audits.Property(x => x.Action).HasMaxLength(32).IsRequired();
        audits.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        audits.HasIndex(x => new { x.TenantId, x.CreatedAt });
        audits.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        audits.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;

        // Phase 1 only: no tenant filters. Do not expose these entities via HTTP.
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<InventoryItem>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.Quantity < 0 || entry.Entity.Price < 0 ||
                entry.Entity.Price > 9999999999999999.99m ||
                decimal.Round(entry.Entity.Price, 2) != entry.Entity.Price)
                throw new InvalidOperationException("Inventory quantity or price is invalid.");

            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            else entry.Property(x => x.CreatedAt).IsModified = false;
            entry.Entity.UpdatedAt = now;
            entry.Entity.RowVersion = Guid.NewGuid();
        }

        foreach (var entry in ChangeTracker.Entries<ItemFile>().Where(x => x.State == EntityState.Added))
            entry.Entity.CreatedAt = now;
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(x => x.State == EntityState.Added))
            entry.Entity.CreatedAt = now;
    }
}
````

## Data/Migrations/20260930075313_InitialCreate.cs

````csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenNet.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.UniqueConstraint("AK_InventoryItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_InventoryItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserTenantMemberships",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTenantMemberships", x => new { x.UserId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_UserTenantMemberships_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserTenantMemberships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoredKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemFiles_InventoryItems_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "InventoryItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                table: "AppUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_UserName",
                table: "AppUsers",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_Sku",
                table: "InventoryItems",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemFiles_StoredKey",
                table: "ItemFiles",
                column: "StoredKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemFiles_TenantId_ItemId",
                table: "ItemFiles",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantMemberships_TenantId",
                table: "UserTenantMemberships",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ItemFiles");

            migrationBuilder.DropTable(
                name: "UserTenantMemberships");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
````

## Data/Migrations/20260930075313_InitialCreate.Designer.cs

````csharp
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TenNet.Data;

#nullable disable

namespace TenNet.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260930075313_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.31");

            modelBuilder.Entity("TenNet.Domain.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(254)
                        .HasColumnType("TEXT");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("TEXT");

                    b.Property<string>("UserName")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Email")
                        .IsUnique();

                    b.HasIndex("UserName")
                        .IsUnique();

                    b.ToTable("AppUsers");
                });

            modelBuilder.Entity("TenNet.Domain.AuditLog", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("EntityId")
                        .HasColumnType("TEXT");

                    b.Property<string>("EntityType")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.HasIndex("TenantId", "CreatedAt");

                    b.ToTable("AuditLogs");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.Property<decimal>("Price")
                        .HasPrecision(18, 2)
                        .HasColumnType("TEXT");

                    b.Property<int>("Quantity")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("RowVersion")
                        .IsConcurrencyToken()
                        .HasColumnType("TEXT");

                    b.Property<string>("Sku")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("TenantId", "Sku")
                        .IsUnique();

                    b.ToTable("InventoryItems");
                });

            modelBuilder.Entity("TenNet.Domain.ItemFile", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("ItemId")
                        .HasColumnType("TEXT");

                    b.Property<string>("OriginalName")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("TEXT");

                    b.Property<long>("Size")
                        .HasColumnType("INTEGER");

                    b.Property<string>("StoredKey")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("StoredKey")
                        .IsUnique();

                    b.HasIndex("TenantId", "ItemId");

                    b.ToTable("ItemFiles");
                });

            modelBuilder.Entity("TenNet.Domain.Tenant", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("TEXT");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasMaxLength(63)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Slug")
                        .IsUnique();

                    b.ToTable("Tenants");
                });

            modelBuilder.Entity("TenNet.Domain.UserTenantMembership", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.HasKey("UserId", "TenantId");

                    b.HasIndex("TenantId");

                    b.ToTable("UserTenantMemberships");
                });

            modelBuilder.Entity("TenNet.Domain.AuditLog", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.AppUser", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");

                    b.Navigation("User");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");
                });

            modelBuilder.Entity("TenNet.Domain.ItemFile", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.InventoryItem", "Item")
                        .WithMany("Files")
                        .HasForeignKey("TenantId", "ItemId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Item");

                    b.Navigation("Tenant");
                });

            modelBuilder.Entity("TenNet.Domain.UserTenantMembership", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.AppUser", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");

                    b.Navigation("User");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.Navigation("Files");
                });
#pragma warning restore 612, 618
        }
    }
}
````

## Data/Migrations/AppDbContextModelSnapshot.cs

````csharp
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TenNet.Data;

#nullable disable

namespace TenNet.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.31");

            modelBuilder.Entity("TenNet.Domain.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(254)
                        .HasColumnType("TEXT");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("TEXT");

                    b.Property<string>("UserName")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Email")
                        .IsUnique();

                    b.HasIndex("UserName")
                        .IsUnique();

                    b.ToTable("AppUsers");
                });

            modelBuilder.Entity("TenNet.Domain.AuditLog", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasMaxLength(32)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("EntityId")
                        .HasColumnType("TEXT");

                    b.Property<string>("EntityType")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.HasIndex("TenantId", "CreatedAt");

                    b.ToTable("AuditLogs");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("TEXT");

                    b.Property<decimal>("Price")
                        .HasPrecision(18, 2)
                        .HasColumnType("TEXT");

                    b.Property<int>("Quantity")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("RowVersion")
                        .IsConcurrencyToken()
                        .HasColumnType("TEXT");

                    b.Property<string>("Sku")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("TenantId", "Sku")
                        .IsUnique();

                    b.ToTable("InventoryItems");
                });

            modelBuilder.Entity("TenNet.Domain.ItemFile", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("ItemId")
                        .HasColumnType("TEXT");

                    b.Property<string>("OriginalName")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("TEXT");

                    b.Property<long>("Size")
                        .HasColumnType("INTEGER");

                    b.Property<string>("StoredKey")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("StoredKey")
                        .IsUnique();

                    b.HasIndex("TenantId", "ItemId");

                    b.ToTable("ItemFiles");
                });

            modelBuilder.Entity("TenNet.Domain.Tenant", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("TEXT");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasMaxLength(63)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Slug")
                        .IsUnique();

                    b.ToTable("Tenants");
                });

            modelBuilder.Entity("TenNet.Domain.UserTenantMembership", b =>
                {
                    b.Property<Guid>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("TenantId")
                        .HasColumnType("TEXT");

                    b.HasKey("UserId", "TenantId");

                    b.HasIndex("TenantId");

                    b.ToTable("UserTenantMemberships");
                });

            modelBuilder.Entity("TenNet.Domain.AuditLog", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.AppUser", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");

                    b.Navigation("User");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");
                });

            modelBuilder.Entity("TenNet.Domain.ItemFile", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.InventoryItem", "Item")
                        .WithMany("Files")
                        .HasForeignKey("TenantId", "ItemId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Item");

                    b.Navigation("Tenant");
                });

            modelBuilder.Entity("TenNet.Domain.UserTenantMembership", b =>
                {
                    b.HasOne("TenNet.Domain.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TenNet.Domain.AppUser", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Tenant");

                    b.Navigation("User");
                });

            modelBuilder.Entity("TenNet.Domain.InventoryItem", b =>
                {
                    b.Navigation("Files");
                });
#pragma warning restore 612, 618
        }
    }
}
````

## Api/TenNet.Api.csproj

````xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.31">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <ProjectReference Include="..\Domain\TenNet.Domain.csproj" />
    <ProjectReference Include="..\Data\TenNet.Data.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <UserSecretsId>TenNet-Development-7bd966bc-2e83-49c0-9ec8-1c9875204251</UserSecretsId>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
````

## Api/Program.cs

````csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TenNet.Api;
using TenNet.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Inventory")
    ?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.");
var sqlite = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
    sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqlite.ConnectionString));
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, app.Configuration, app.Environment);
}

app.MapGet("/hello", () => Results.Ok(new { message = "Hello from TenNet!" }));

app.Run();
````

## Api/DbSeeder.cs

````csharp
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
}
````

## Api/appsettings.json

````json
{
  "ConnectionStrings": {
    "Inventory": "Data Source=tennet.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
````

## Tests/TenNet.Tests.csproj

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Api\TenNet.Api.csproj" />
  </ItemGroup>

</Project>
````

## Tests/DataModelTests.cs

````csharp
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
````

## Tests/README.md

````markdown
# Tests

Phase 1 adds real SQLite tests for migrations, seed idempotence, password hashing,
concurrency conflicts, composite file foreign keys, SKU uniqueness, restricted
deletion and the Development-only seed guard. Run `dotnet test TenNet.sln`.
Phase 6 adds WebApplicationFactory, SQLite in-memory and isolation tests.
Phase 7 adds file validation tests and a fake IFileStorage implementation.
````
