# Phase 3 — Complete Source Handoff

Every source and configuration file created or changed in Phase 3 is included below in full, with no ellipses.

## File Manifest

- [Domain/TenantContext.cs](D:/Desktop/TenNet/Domain/TenantContext.cs)
- [Data/TenantLookupContext.cs](D:/Desktop/TenNet/Data/TenantLookupContext.cs)
- [Api/Tenancy/ITenantResolver.cs](D:/Desktop/TenNet/Api/Tenancy/ITenantResolver.cs)
- [Api/Tenancy/HeaderTenantResolver.cs](D:/Desktop/TenNet/Api/Tenancy/HeaderTenantResolver.cs)
- [Api/Tenancy/SubdomainTenantResolver.cs](D:/Desktop/TenNet/Api/Tenancy/SubdomainTenantResolver.cs)
- [Api/Tenancy/CompositeTenantResolver.cs](D:/Desktop/TenNet/Api/Tenancy/CompositeTenantResolver.cs)
- [Api/Tenancy/TenantLookupService.cs](D:/Desktop/TenNet/Api/Tenancy/TenantLookupService.cs)
- [Api/Tenancy/TenantMiddleware.cs](D:/Desktop/TenNet/Api/Tenancy/TenantMiddleware.cs)
- [Api/Tenancy/TenantController.cs](D:/Desktop/TenNet/Api/Tenancy/TenantController.cs)
- [Api/Program.cs](D:/Desktop/TenNet/Api/Program.cs)
- [Api/appsettings.json](D:/Desktop/TenNet/Api/appsettings.json)
- [Tests/AuthAppFactory.cs](D:/Desktop/TenNet/Tests/AuthAppFactory.cs)
- [Tests/TenantResolutionTests.cs](D:/Desktop/TenNet/Tests/TenantResolutionTests.cs)
- [docs/phase-3.md](D:/Desktop/TenNet/docs/phase-3.md)
- [README.md](D:/Desktop/TenNet/README.md)

---

## Domain/TenantContext.cs

```csharp
namespace TenNet.Domain;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantName { get; }
    bool IsSet { get; }
}

public interface ITenantContextSetter
{
    void SetTenant(Guid tenantId, string tenantName);
}

public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }
    public string? TenantName { get; private set; }
    public bool IsSet { get; private set; }

    public void SetTenant(Guid tenantId, string tenantName)
    {
        if (IsSet)
        {
            throw new InvalidOperationException("Tenant context has already been set for this request.");
        }
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        }
        if (string.IsNullOrWhiteSpace(tenantName))
        {
            throw new ArgumentException("TenantName cannot be null or whitespace.", nameof(tenantName));
        }

        TenantId = tenantId;
        TenantName = tenantName.Trim();
        IsSet = true;
    }
}
```

---

## Data/TenantLookupContext.cs

```csharp
using Microsoft.EntityFrameworkCore;
using TenNet.Domain;

namespace TenNet.Data;

public sealed class TenantLookupContext(DbContextOptions<TenantLookupContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserTenantMembership> UserTenantMemberships => Set<UserTenantMembership>();

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

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
```

---

## Api/Tenancy/ITenantResolver.cs

```csharp
using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public enum TenantResolutionStatus
{
    Success,
    Missing,
    Invalid
}

public sealed class TenantResolutionResult
{
    public TenantResolutionStatus Status { get; }
    public Guid? TenantId { get; }
    public string? RawIdentifier { get; }
    public string? ErrorMessage { get; }

    private TenantResolutionResult(TenantResolutionStatus status, Guid? tenantId, string? rawIdentifier, string? errorMessage)
    {
        Status = status;
        TenantId = tenantId;
        RawIdentifier = rawIdentifier;
        ErrorMessage = errorMessage;
    }

    public static TenantResolutionResult Success(Guid tenantId, string rawIdentifier) =>
        new(TenantResolutionStatus.Success, tenantId, rawIdentifier, null);

    public static TenantResolutionResult Missing(string message = "Tenant identifier is missing.") =>
        new(TenantResolutionStatus.Missing, null, null, message);

    public static TenantResolutionResult Invalid(string errorMessage) =>
        new(TenantResolutionStatus.Invalid, null, null, errorMessage);
}

public interface ITenantResolver
{
    Task<TenantResolutionResult> ResolveAsync(HttpContext context);
}
```

---

## Api/Tenancy/HeaderTenantResolver.cs

```csharp
using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public sealed class HeaderTenantResolver : ITenantResolver
{
    public const string HeaderName = "X-Tenant-Id";
    public const int MaxHeaderLength = 64;

    public Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        if (values.Count == 0)
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        if (values.Count > 1)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Duplicate {HeaderName} header detected."));
        }

        var rawHeader = values[0];
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Missing {HeaderName} header."));
        }

        // Detect comma-separated multiple GUID values (e.g. from reverse proxy header folding)
        if (rawHeader.Contains(',') && Guid.TryParse(rawHeader.Split(',')[0].Trim(), out _))
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Duplicate {HeaderName} header detected."));
        }

        var trimmed = rawHeader.Trim();
        if (trimmed.Length > MaxHeaderLength)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"{HeaderName} header exceeds maximum allowed length of {MaxHeaderLength} characters."));
        }

        if (trimmed.Length != 36 || !Guid.TryParseExact(trimmed, "D", out var tenantId) || tenantId == Guid.Empty)
        {
            return Task.FromResult(TenantResolutionResult.Invalid($"Malformed or invalid GUID in {HeaderName} header."));
        }

        return Task.FromResult(TenantResolutionResult.Success(tenantId, trimmed));
    }
}
```

---

## Api/Tenancy/SubdomainTenantResolver.cs

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TenNet.Api.Tenancy;

public sealed class SubdomainOptions
{
    public const string SectionName = "Tenancy:Subdomain";

    public bool Enabled { get; set; } = false;
    public string BaseDomain { get; set; } = "example.com";
}

/// <summary>
/// Disabled stub for subdomain-based tenant resolution.
/// Configuration:
/// - Set "Tenancy:Subdomain:Enabled" to true (default: false).
/// - Set "Tenancy:Subdomain:BaseDomain" to the root domain (e.g. "tennet.example" or "localhost").
/// When enabled, this resolver extracts the tenant slug from the host header (e.g. "acme.tennet.example" -> "acme").
/// </summary>
public sealed class SubdomainTenantResolver(IOptions<SubdomainOptions> options, ILogger<SubdomainTenantResolver> logger) : ITenantResolver
{
    private readonly SubdomainOptions _options = options.Value;

    public Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            // Subdomain resolution is disabled by default in configuration.
            return Task.FromResult(TenantResolutionResult.Missing("Subdomain tenant resolution is disabled."));
        }

        var host = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(TenantResolutionResult.Missing("Host header is missing."));
        }

        if (!host.EndsWith(_options.BaseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(TenantResolutionResult.Missing($"Host '{host}' does not match configured base domain '{_options.BaseDomain}'."));
        }

        var prefixLength = host.Length - _options.BaseDomain.Length;
        if (prefixLength <= 1)
        {
            return Task.FromResult(TenantResolutionResult.Missing("No subdomain prefix found on host."));
        }

        var subdomain = host[..(prefixLength - 1)].Trim();
        if (string.IsNullOrWhiteSpace(subdomain) || subdomain.Contains('.'))
        {
            return Task.FromResult(TenantResolutionResult.Invalid("Malformed subdomain format."));
        }

        logger.LogInformation("Subdomain tenant '{Subdomain}' detected (stub mode).", subdomain);
        return Task.FromResult(TenantResolutionResult.Missing($"Subdomain '{subdomain}' identified but resolver stub does not map slugs to GUIDs yet."));
    }
}
```

---

## Api/Tenancy/CompositeTenantResolver.cs

```csharp
using Microsoft.AspNetCore.Http;

namespace TenNet.Api.Tenancy;

public sealed class CompositeTenantResolver(IEnumerable<ITenantResolver> resolvers) : ITenantResolver
{
    private readonly IReadOnlyList<ITenantResolver> _resolvers = resolvers.ToList();

    public async Task<TenantResolutionResult> ResolveAsync(HttpContext context)
    {
        TenantResolutionResult? firstMissing = null;

        foreach (var resolver in _resolvers)
        {
            var result = await resolver.ResolveAsync(context);
            if (result.Status == TenantResolutionStatus.Success)
            {
                return result;
            }

            if (result.Status == TenantResolutionStatus.Invalid)
            {
                // Fail closed immediately on malformed, duplicate, or oversized inputs
                return result;
            }

            firstMissing ??= result;
        }

        return firstMissing ?? TenantResolutionResult.Missing("Missing required tenant identifier.");
    }
}
```

---

## Api/Tenancy/TenantLookupService.cs

```csharp
using Microsoft.EntityFrameworkCore;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Tenancy;

public sealed record TenantLookupResult(bool Exists, bool IsMember, Tenant? Tenant);

public interface ITenantLookupService
{
    Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantLookupResult> ValidateTenantMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class TenantLookupService(TenantLookupContext db) : ITenantLookupService
{
    public async Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    public async Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await db.UserTenantMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);
    }

    public async Task<TenantLookupResult> ValidateTenantMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return new TenantLookupResult(Exists: false, IsMember: false, Tenant: null);
        }

        var isMember = await db.UserTenantMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

        return new TenantLookupResult(Exists: true, IsMember: isMember, Tenant: tenant);
    }
}
```

---

## Api/Tenancy/TenantMiddleware.cs

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TenNet.Domain;

namespace TenNet.Api.Tenancy;

public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    private static readonly HashSet<string> AnonymousAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "/hello",
        "/health",
        "/api/auth/login",
        "/api/auth/me"
    };

    public async Task InvokeAsync(
        HttpContext context,
        CompositeTenantResolver resolver,
        ITenantLookupService lookupService,
        ITenantContextSetter contextSetter)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        // Exempt endpoints bypass tenant resolution
        if (AnonymousAllowlist.Contains(path) || IsAllowAnonymous(context))
        {
            await next(context);
            return;
        }

        // 1. Resolve tenant identifier from request
        TenantResolutionResult resolution;
        try
        {
            resolution = await resolver.ResolveAsync(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant resolver failed unexpectedly.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred during tenant resolution.")
                .ExecuteAsync(context);
            return;
        }

        if (resolution.Status == TenantResolutionStatus.Missing)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: resolution.ErrorMessage ?? "Missing required tenant identifier.")
                .ExecuteAsync(context);
            return;
        }

        if (resolution.Status == TenantResolutionStatus.Invalid)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: resolution.ErrorMessage ?? "Invalid tenant identifier.")
                .ExecuteAsync(context);
            return;
        }

        var tenantId = resolution.TenantId!.Value;

        // 2. Verify authentication
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
                .ExecuteAsync(context);
            return;
        }

        var subClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(subClaim, out var userId))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
                .ExecuteAsync(context);
            return;
        }

        // 3. Lookup tenant and verify user membership
        TenantLookupResult lookup;
        try
        {
            lookup = await lookupService.ValidateTenantMembershipAsync(userId, tenantId, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during tenant lookup and membership validation.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred during tenant resolution.")
                .ExecuteAsync(context);
            return;
        }

        if (!lookup.Exists)
        {
            // Unknown tenant returns 404
            await Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Tenant not found")
                .ExecuteAsync(context);
            return;
        }

        if (!lookup.IsMember)
        {
            // Non-member tenant returns 403
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "User is not a member of the requested tenant.")
                .ExecuteAsync(context);
            return;
        }

        // 4. Set scoped tenant context (throws if set twice)
        try
        {
            contextSetter.SetTenant(lookup.Tenant!.Id, lookup.Tenant.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set tenant context.");
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred while setting tenant context.")
                .ExecuteAsync(context);
            return;
        }

        // 5. Establish logging scope with TenantId and UserId (no secrets)
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = lookup.Tenant!.Id,
            ["TenantName"] = lookup.Tenant.Name,
            ["UserId"] = userId
        }))
        {
            await next(context);
        }
    }

    private static bool IsAllowAnonymous(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
```

---

## Api/Tenancy/TenantController.cs

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TenNet.Domain;

namespace TenNet.Api.Tenancy;

[ApiController]
[Route("api/tenant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TenantController(ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        if (!tenantContext.IsSet || tenantContext.TenantId is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Tenant context is not set.");
        }

        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        Guid.TryParse(subClaim, out var userId);
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;

        return Ok(new
        {
            tenantId = tenantContext.TenantId.Value,
            tenantName = tenantContext.TenantName,
            userId,
            email
        });
    }
}
```

---

## Api/Program.cs

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Threading.RateLimiting;
using TenNet.Api;
using TenNet.Api.Authentication;
using TenNet.Api.Tenancy;
using TenNet.Data;
using TenNet.Domain;

var builder = WebApplication.CreateBuilder(args);
var jwt = JwtSettings.Load(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(new LoginPasswordVerifier());
builder.Services.AddScoped<AuthService>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.SaveToken = false;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = jwt.ValidationParameters();
});
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await Results.Problem(statusCode: 429, title: "Too many requests").ExecuteAsync(context.HttpContext);
    };
});
var connectionString = builder.Configuration.GetConnectionString("Inventory")
    ?? throw new InvalidOperationException("ConnectionStrings:Inventory is required.");
var sqlite = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
    sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqlite.ConnectionString));
builder.Services.AddDbContext<TenantLookupContext>(options => options.UseSqlite(sqlite.ConnectionString));

// Phase 3: Tenant resolution and scoped context
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantLookupService, TenantLookupService>();

builder.Services.Configure<SubdomainOptions>(builder.Configuration.GetSection(SubdomainOptions.SectionName));
builder.Services.AddSingleton<HeaderTenantResolver>();
builder.Services.AddSingleton<SubdomainTenantResolver>();
builder.Services.AddScoped<CompositeTenantResolver>(sp =>
    new CompositeTenantResolver([sp.GetRequiredService<HeaderTenantResolver>(), sp.GetRequiredService<SubdomainTenantResolver>()]));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, app.Configuration, app.Environment);
}

app.UseExceptionHandler(handler => handler.Run(context =>
    Results.Problem(statusCode: 500, title: "An unexpected error occurred").ExecuteAsync(context)));
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/hello", () => Results.Ok(new { message = "Hello from TenNet!" })).AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program { }
```

---

## Api/appsettings.json

```json
{
  "Jwt": {
    "Issuer": "https://tennet.local",
    "Audience": "tennet-api"
  },
  "ConnectionStrings": {
    "Inventory": "Data Source=tennet.db"
  },
  "Tenancy": {
    "Subdomain": {
      "Enabled": false,
      "BaseDomain": "tennet.local"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

---

## Tests/AuthAppFactory.cs

```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenNet.Api;
using TenNet.Data;

namespace TenNet.Tests;

public sealed class AuthAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public AuthAppFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Issuer", "https://tennet.test");
        builder.UseSetting("Jwt:Audience", "tennet-tests");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<DbContextOptions<TenantLookupContext>>();
            services.RemoveAll<TenantLookupContext>();
            services.AddDbContext<TenantLookupContext>(options => options.UseSqlite(connection));
        });
    }

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Seed:Passwords:alice"] = Password,
                ["Seed:Passwords:bob"] = Password,
                ["Seed:Passwords:carol"] = Password
            }).Build();
        await DbSeeder.SeedAsync(db, configuration, new SeedEnvironment());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }

    private sealed class SeedEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TenNet.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
```

---

## Tests/TenantResolutionTests.cs

```csharp
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
```
