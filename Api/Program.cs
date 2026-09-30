using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Threading.RateLimiting;
using TenNet.Api;
using TenNet.Api.Authentication;
using TenNet.Api.Inventory;
using TenNet.Api.Storage;
using TenNet.Api.Tenancy;
using TenNet.Data;
using TenNet.Domain;

var builder = WebApplication.CreateBuilder(args);

// Phase 9: Remove Server header
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 15 * 1024 * 1024; // 15 MB request limit
});

var jwt = JwtSettings.Load(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(new LoginPasswordVerifier());
builder.Services.AddScoped<AuthService>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Phase 8/9: Exact-origin CORS policy
const string FrontendCorsPolicy = "FrontendCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type", "X-Tenant-Id")
              .WithExposedHeaders("Retry-After");
    });
});

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
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests").ExecuteAsync(context.HttpContext);
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

// Phase 5: Inventory service
builder.Services.AddScoped<IItemService, ItemService>();

// Phase 7: File storage (S3 when configured, InMemory fallback for tests)
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
var storageConfig = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>();
if (storageConfig != null && (!string.IsNullOrEmpty(storageConfig.AccessKey) || !string.IsNullOrEmpty(storageConfig.ServiceUrl)))
{
    Console.WriteLine($"[Storage] Configured S3FileStorage -> Bucket: '{storageConfig.BucketName}', Region: '{storageConfig.Region}'");
    builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
}
else
{
    Console.WriteLine("[Storage] Configured InMemoryFileStorage (AWS S3 credentials not found at startup).");
    builder.Services.AddSingleton<IFileStorage, InMemoryFileStorage>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, app.Configuration, app.Environment);
}

// Phase 9: Global Exception and Security Headers
app.UseExceptionHandler(handler => handler.Run(context =>
    Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred").ExecuteAsync(context)));

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'none'; default-src 'self'");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseCors(FrontendCorsPolicy);
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
