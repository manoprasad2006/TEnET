using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenNet.Api.Authentication;
using TenNet.Api.Inventory;
using TenNet.Api.Storage;
using TenNet.Data;

namespace TenNet.Tests;

public sealed class FileStorageTests
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

    private static async Task<Guid> GetFirstItemIdAsync(HttpClient client, Guid tenantId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        req.Headers.Add("X-Tenant-Id", tenantId.ToString("D"));
        var resp = await client.SendAsync(req);
        var paged = (await resp.Content.ReadFromJsonAsync<PagedResult<ItemResponse>>())!;
        return paged.Items[0].Id;
    }

    [Fact]
    public async Task UploadValidPngAndDownloadSucceedsWithAudit()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, userId, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];
        var itemId = await GetFirstItemIdAsync(client, acmeId);

        // Valid PNG bytes
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "diagram.png");

        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{itemId}/files");
        uploadReq.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        uploadReq.Content = content;

        var uploadResp = await client.SendAsync(uploadReq);
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
        var fileResponse = (await uploadResp.Content.ReadFromJsonAsync<FileResponse>())!;
        Assert.Equal("diagram.png", fileResponse.OriginalName);
        Assert.Equal("image/png", fileResponse.ContentType);

        // Download the file
        using var downloadReq = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileResponse.Id}/download");
        downloadReq.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        var downloadResp = await client.SendAsync(downloadReq);
        Assert.Equal(HttpStatusCode.OK, downloadResp.StatusCode);
        var downloadedBytes = await downloadResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(pngBytes, downloadedBytes);

        // Verify Audit Log
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BypassTenantFilter = true;
        var uploadAudit = await db.AuditLogs.SingleOrDefaultAsync(x => x.EntityId == fileResponse.Id && x.Action == "FILE_UPLOAD");
        Assert.NotNull(uploadAudit);
        Assert.Equal(userId, uploadAudit.UserId);
        Assert.Equal(acmeId, uploadAudit.TenantId);
    }

    [Fact]
    public async Task UploadRejectsBadMagicBytesDoubleExtensionsAndDisallowedExtensions()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];
        var itemId = await GetFirstItemIdAsync(client, acmeId);

        // 1. Bad magic bytes: executable masquerading as PNG
        var fakePngBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }; // MZ header (PE executable)
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(fakePngBytes), "file", "fake.png");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{itemId}/files");
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            req.Content = content;
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // 2. Double extension: payload.php.png
        var validPngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(validPngBytes), "file", "payload.php.png");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{itemId}/files");
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            req.Content = content;
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // 3. Disallowed extension: script.sh
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(new byte[] { 0x23, 0x21, 0x2F }), "file", "script.sh");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{itemId}/files");
            req.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
            req.Content = content;
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
    }

    [Fact]
    public async Task CrossTenantFileAccessIsBlocked()
    {
        using var factory = new AuthAppFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        var (token, _, tenants) = await LoginAsync(client, factory, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var acmeId = tenants["Acme"];
        var globexId = tenants["Globex"];

        var acmeItemId = await GetFirstItemIdAsync(client, acmeId);

        // Upload a file to Acme
        var txtBytes = "Valid log content"u8.ToArray();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(txtBytes), "file", "notes.txt");

        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{acmeItemId}/files");
        uploadReq.Headers.Add("X-Tenant-Id", acmeId.ToString("D"));
        uploadReq.Content = content;

        var uploadResp = await client.SendAsync(uploadReq);
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
        var fileResponse = (await uploadResp.Content.ReadFromJsonAsync<FileResponse>())!;

        // Now Globex attempts to download Acme's file -> 404
        using var globexReq = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileResponse.Id}/download");
        globexReq.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
        var globexResp = await client.SendAsync(globexReq);
        Assert.Equal(HttpStatusCode.NotFound, globexResp.StatusCode);

        // Globex attempts to delete Acme's file -> 404
        using var globexDeleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/files/{fileResponse.Id}");
        globexDeleteReq.Headers.Add("X-Tenant-Id", globexId.ToString("D"));
        var globexDeleteResp = await client.SendAsync(globexDeleteReq);
        Assert.Equal(HttpStatusCode.NotFound, globexDeleteResp.StatusCode);
    }
}
