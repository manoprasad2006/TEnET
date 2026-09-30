# Phase 2 — complete source handoff

Every source/configuration file created or changed in Phase 2 is included below in full.
The complete [README.md](D:/Desktop/TenNet/README.md) is provided separately so demo credentials remain documented only there.
This is the Phase 2 snapshot; future changes belong in the original files.

## File manifest

- [Api/TenNet.Api.csproj](D:/Desktop/TenNet/Api/TenNet.Api.csproj)
- [Api/Program.cs](D:/Desktop/TenNet/Api/Program.cs)
- [Api/appsettings.json](D:/Desktop/TenNet/Api/appsettings.json)
- [Api/Authentication/JwtSettings.cs](D:/Desktop/TenNet/Api/Authentication/JwtSettings.cs)
- [Api/Authentication/AuthDtos.cs](D:/Desktop/TenNet/Api/Authentication/AuthDtos.cs)
- [Api/Authentication/LoginPasswordVerifier.cs](D:/Desktop/TenNet/Api/Authentication/LoginPasswordVerifier.cs)
- [Api/Authentication/AuthService.cs](D:/Desktop/TenNet/Api/Authentication/AuthService.cs)
- [Api/Authentication/AuthController.cs](D:/Desktop/TenNet/Api/Authentication/AuthController.cs)
- [Tests/TenNet.Tests.csproj](D:/Desktop/TenNet/Tests/TenNet.Tests.csproj)
- [Tests/AuthAppFactory.cs](D:/Desktop/TenNet/Tests/AuthAppFactory.cs)
- [Tests/AuthTests.cs](D:/Desktop/TenNet/Tests/AuthTests.cs)
- [Tests/README.md](D:/Desktop/TenNet/Tests/README.md)
- [.env.example](D:/Desktop/TenNet/.env.example)
- [docs/phase-2.md](D:/Desktop/TenNet/docs/phase-2.md)
- [README.md](D:/Desktop/TenNet/README.md) — updated complete runbook.
- [docs/phase-2-code.md](D:/Desktop/TenNet/docs/phase-2-code.md) — this generated handoff.

The external development user-secrets store now contains a random JWT signing key.
Secret values, database files and ignored build output are excluded.

## Api/TenNet.Api.csproj

`````xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.31" />
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
`````

## Api/Program.cs

`````csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Threading.RateLimiting;
using TenNet.Api;
using TenNet.Api.Authentication;
using TenNet.Data;

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
// Phase 3 inserts tenant resolution here, before authorization.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/hello", () => Results.Ok(new { message = "Hello from TenNet!" })).AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program { }
`````

## Api/appsettings.json

`````json
{
  "Jwt": {
    "Issuer": "https://tennet.local",
    "Audience": "tennet-api"
  },
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
`````

## Api/Authentication/JwtSettings.cs

`````csharp
using Microsoft.IdentityModel.Tokens;

namespace TenNet.Api.Authentication;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required SymmetricSecurityKey SigningKey { get; init; }
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(15);
    public static TimeSpan ClockSkew => TimeSpan.FromSeconds(30);

    public static JwtSettings Load(IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 200 ||
            string.IsNullOrWhiteSpace(audience) || audience.Length > 200)
            throw new InvalidOperationException("Configure Jwt:Issuer and Jwt:Audience.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuration["Jwt:SigningKey"] ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be a Base64-encoded random key.");
        }
        if (key.Length < 32)
            throw new InvalidOperationException("Set Jwt:SigningKey to at least 32 random bytes, Base64-encoded, using user-secrets or environment variables.");

        return new JwtSettings { Issuer = issuer, Audience = audience, SigningKey = new SymmetricSecurityKey(key) };
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = ClockSkew,
        NameClaimType = "sub"
    };
}
`````

## Api/Authentication/AuthDtos.cs

`````csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TenNet.Api.Authentication;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed record TenantSummary(Guid Id, string Name);
public sealed record UserSummary(Guid Id, string Email, string UserName);
public sealed record MeResponse(UserSummary User, IReadOnlyList<TenantSummary> Tenants);
public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAt,
    UserSummary User, IReadOnlyList<TenantSummary> Tenants);
`````

## Api/Authentication/LoginPasswordVerifier.cs

`````csharp
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
`````

## Api/Authentication/AuthService.cs

`````csharp
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Authentication;

public sealed class AuthService(AppDbContext db, JwtSettings jwt, LoginPasswordVerifier passwords)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var valid = passwords.Verify(user, request.Password);

        if (!valid)
        {
            var remaining = TimeSpan.FromMilliseconds(500) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            return null;
        }

        var tenants = await GetTenantsAsync(user!.Id, cancellationToken);
        var now = DateTime.UtcNow;
        var expires = now.Add(JwtSettings.Lifetime);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
             new Claim(JwtRegisteredClaimNames.Email, user.Email)],
            notBefore: now, expires: expires,
            signingCredentials: new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires,
            Summarize(user), tenants);
    }

    public async Task<MeResponse?> MeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return user is null ? null : new MeResponse(Summarize(user), await GetTenantsAsync(user.Id, cancellationToken));
    }

    private async Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Bound the query and fail explicitly rather than silently hiding memberships.
        var tenants = await db.UserTenantMemberships.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Tenant.Name).ThenBy(x => x.TenantId)
            .Select(x => new TenantSummary(x.TenantId, x.Tenant.Name)).Take(101).ToListAsync(cancellationToken);
        if (tenants.Count > 100) throw new InvalidOperationException("Membership response limit exceeded.");
        return tenants;
    }

    private static UserSummary Summarize(AppUser user) => new(user.Id, user.Email, user.UserName);
}
`````

## Api/Authentication/AuthController.cs

`````csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TenNet.Api.Authentication;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.LoginAsync(request, cancellationToken);
        if (response is null) return Problem(statusCode: 401, title: "Invalid credentials");
        return Ok(response);
    }

    // Protected by the fallback policy; no tenant header required for this endpoint.
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId)) return Unauthorized();
        var response = await auth.MeAsync(userId, cancellationToken);
        if (response is null) return Unauthorized();
        return Ok(response);
    }
}
`````

## Tests/TenNet.Tests.csproj

`````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.31" />
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
`````

## Tests/AuthAppFactory.cs

`````csharp
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
`````

## Tests/AuthTests.cs

`````csharp
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
`````

## Tests/README.md

`````markdown
# Tests

Phase 1 adds real SQLite tests for migrations, seed idempotence, password hashing,
concurrency conflicts, composite file foreign keys, SKU uniqueness, restricted
deletion and the Development-only seed guard. Run `dotnet test TenNet.sln`.
Phase 2 adds WebApplicationFactory tests with independent SQLite in-memory
databases, real JWT validation, membership refresh, login DTO validation,
generic failure timing, fallback authorization and login rate limiting.
Run `dotnet test TenNet.sln --filter FullyQualifiedName~AuthTests` for auth only.
Phase 6 expands this into the complete tenant isolation test suite.
Phase 7 adds file validation tests and a fake IFileStorage implementation.
`````

## .env.example

`````text
# ASP.NET Core does not automatically load .env files.
# Supply through the process environment or use dotnet user-secrets in Development.
# Generate at least 32 cryptographically random bytes and Base64-encode them.
Jwt__SigningKey=
Jwt__Issuer=https://tennet.local
Jwt__Audience=tennet-api
# MinIO settings will be added in Phase 7.
`````

## docs/phase-2.md

`````markdown
# Phase 2 — Authentication with JWT and memberships

## 1. Exact commands

The code is implemented; no schema migration is needed. Stop any running API
before building on Windows because its executable is locked while running.

```powershell
Set-Location D:\Desktop\TenNet
dotnet restore .\TenNet.sln
dotnet build .\TenNet.sln --no-restore
dotnet test .\TenNet.sln --no-build --no-restore
```

New dependencies already pinned in project files:

```powershell
dotnet add .\Api\TenNet.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.31
dotnet add .\Tests\TenNet.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 8.0.31
```

Generate a signing key once, if one is not already configured. This was done on
the current machine. Demo user passwords and their setup commands remain only
in the root README.

```powershell
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$rng.Dispose()
$jwtKey = [Convert]::ToBase64String($keyBytes)
dotnet user-secrets set 'Jwt:SigningKey' $jwtKey --project .\Api | Out-Null
Remove-Variable jwtKey, keyBytes
dotnet run --project .\Api --no-build --launch-profile http
```

Configuration equivalent: `Jwt__SigningKey` in the process environment. The value
must be Base64 of at least 32 random bytes. The application rejects a missing or
weak key in all environments. Never print the key, token or password.

## 2. Every file with full code

The full source is in [phase-2-code.md](phase-2-code.md). The updated root README
is linked separately to keep demo credentials documented only there. No Domain,
Data or migration files were changed in this phase.

## 3. Key pieces

- **JwtSettings:** validates configuration at startup. The same issuer, audience,
  key, HS256 algorithm and time rules govern issuance and bearer validation.
- **LoginPasswordVerifier:** creates one random dummy hash at startup. Unknown
  users still undergo password verification; failed requests have a 500 ms floor.
- **AuthService:** issues a 15-minute JWT with identity claims only. Login and
  `/me` fetch the user's memberships from the DB with a bounded query.
- **AuthController:** binds validated DTOs and rejects unknown JSON properties.
  Login errors are generic and authentication responses are marked no-store.
- **Fallback authorization:** every mapped endpoint requires authentication unless
  explicitly anonymous. Only login, `/hello` and `/health` are anonymous here.
- **Login limiter:** five requests per minute per connection IP, no waiting queue,
  then 429 with Retry-After. Untrusted forwarding headers cannot select the IP.
- **Pipeline:** exception handling, routing, authentication, rate limiting,
  authorization, then endpoints. Phase 3 inserts tenant middleware after authentication.
- **Tests:** real bearer validation runs through WebApplicationFactory. Each
  fixture uses random secrets and its own open SQLite in-memory connection.

## 4. How to verify

Use a fresh one-minute rate-limit window before this sequence. More than five
login requests in a minute return 429; obey Retry-After before trying again.
Run commands without PowerShell transcription, curl verbose mode or request logging.

In a second PowerShell terminal:

```powershell
$base = 'http://127.0.0.1:5184'
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/health"
# Expected: 200
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me"
# Expected: 401

$email = Read-Host 'Demo email (see README)'
$securePassword = Read-Host 'Demo password (see README)' -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $securePassword).Password
$body = @{ email = $email; password = $password } | ConvertTo-Json -Compress
$loginJson = $body | curl.exe -sS "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
$login = $loginJson | ConvertFrom-Json
if (-not $login.accessToken) { throw 'Login failed; verify credentials and rate-limit window.' }
$token = $login.accessToken
$login.user
$login.tenants
Remove-Variable password, securePassword, body, loginJson, login
# Never display $token.

curl.exe -sS "$base/api/auth/me" -H "Authorization: Bearer $token"
# Expected: current user and only their memberships. No X-Tenant-Id needed yet.

$badBody = @{ email = $email; password = 'incorrect' } | ConvertTo-Json -Compress
$badBody | curl.exe -sS -w '\n%{http_code}\n' "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
# Expected: 401, title "Invalid credentials".
$unknownBody = @{ email = 'unknown@tennet.example'; password = 'incorrect' } | ConvertTo-Json -Compress
$unknownBody | curl.exe -sS -w '\n%{http_code}\n' "$base/api/auth/login" -H 'Content-Type: application/json' --data-binary '@-'
# Expected: same status and error shape. Trace IDs differ by request.

$parts = $token.Split('.')
$first = if ($parts[2][0] -eq 'A') { 'B' } else { 'A' }
$parts[2] = $first + $parts[2].Substring(1)
$tampered = $parts -join '.'
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me" -H "Authorization: Bearer $tampered"
# Expected: 401. The signature was actually changed, not just its padding bits.
Remove-Variable parts, tampered
```

For a live expiry check, retain `$token` in this terminal and repeat the following
at least **15 minutes 31 seconds after login**. Do not log in again or replace it:

```powershell
curl.exe -sS -o NUL -w '%{http_code}\n' "$base/api/auth/me" -H "Authorization: Bearer $token"
# Expected: 401 after expiry plus the 30-second clock skew.
Remove-Variable token
```

The automated test uses an already-expired token signed with the real test key,
so expiry can also be verified immediately:

```powershell
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~InvalidTokensAreRejected
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~LoginIsLimitedToFiveAttempts
dotnet test .\TenNet.sln --no-build --filter FullyQualifiedName~AuthTests
dotnet list .\TenNet.sln package --vulnerable --include-transitive
```

Startup failure with no production key (no secret values are exposed):

```powershell
Push-Location .\Api
dotnet .\bin\Debug\net8.0\TenNet.Api.dll --environment Production --Jwt:SigningKey=
Pop-Location
```

Expected: nonzero exit and the generic key configuration error before listening.
The server-side startup exception is visible in the terminal, never an HTTP response.

### Verified results on 30 September 2026

- [x] Build: zero warnings and errors.
- [x] Full suite: 26 passed, 0 failed, 0 skipped.
- [x] Alice login and `/me`: Acme and Globex; Bob: Initech.
- [x] Live no-token and tampered-token requests: 401.
- [x] Live wrong-password and unknown-email: 401, generic title; 524/513 ms.
- [x] Expired, tampered, wrong-audience, wrong-issuer, wrong-key, future and
      unsigned JWTs: all rejected through the HTTP pipeline in tests.
- [x] Membership removal is reflected by `/me` using the existing token.
- [x] Invalid DTOs rejected; sixth login attempt returns 429 with Retry-After.
- [x] Empty production signing key prevents startup.
- [x] No known vulnerable packages reported by the configured NuGet sources.

## 5. Residual risks

**How could a malicious tenant bypass this?** Authentication identifies a user;
tenant authorization and data isolation are still absent. No tenant-data routes
exist yet. Phase 3 must check membership before setting tenant context; Phase 4
must filter reads and enforce writes even if controller code is wrong.

- The failure timing floor reduces enumeration signals but cannot guarantee
  identical wall-clock times under load, different hash settings or network jitter.
- The limiter is in-memory and per process. Distributed IPs and multiple replicas
  require shared abuse controls; trusted proxy configuration must be explicit.
- Access tokens remain valid until expiry without a revocation mechanism.
- Local HTTP is for loopback development. HTTPS/HSTS and broader hardening remain
  later phases; do not expose these credentials or bearer tokens over public HTTP.
- Membership lists support at most 100 tenants per user; larger accounts get a
  generic server error until a paginated membership response is designed.
- SQL uniqueness is still case-sensitive; current seeded emails are lowercase.
  Any future account creation must enforce a consistent email normalization rule.
- No refresh tokens, password reset, MFA, production account provisioning or
  automatic password-hash upgrades are implemented in this hackathon phase.

Finbuckle remains a reference for tenant resolution in Phase 3. Phase 2 uses
standard ASP.NET Core JWT bearer authentication and the existing membership data.
`````
