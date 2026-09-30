using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Storage;

public sealed record FileResponse(
    Guid Id,
    Guid ItemId,
    string OriginalName,
    string ContentType,
    long Size,
    DateTime CreatedAt
);

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FilesController(
    AppDbContext db,
    IFileStorage storage,
    ITenantContext tenantContext) : ControllerBase
{
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private const int MaxFilesPerItem = 10;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".pdf", ".csv", ".txt"
    };

    [HttpPost("api/items/{itemId:guid}/files")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<FileResponse>> UploadFile(
        Guid itemId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "File is empty or missing.");
        }

        if (file.Length > MaxFileSize)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "File exceeds 10 MB maximum allowed size.");
        }

        // Verify item exists in current tenant (filtered context)
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Item not found");
        }

        var existingCount = await db.ItemFiles.CountAsync(x => x.ItemId == itemId, cancellationToken);
        if (existingCount >= MaxFilesPerItem)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: $"Item already has the maximum of {MaxFilesPerItem} files.");
        }

        var rawName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Filename is invalid.");
        }

        // Check for double extensions (e.g. payload.php.png)
        var nameParts = rawName.Split('.');
        if (nameParts.Length > 2)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Double extensions are not allowed.");
        }

        var extension = Path.GetExtension(rawName);
        if (!AllowedExtensions.Contains(extension))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: $"Extension '{extension}' is not permitted.");
        }

        // Validate magic bytes
        using var stream = file.OpenReadStream();
        var headerBytes = new byte[8];
        var read = await stream.ReadAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken);
        stream.Position = 0;

        if (!IsValidMagicBytes(extension, headerBytes, read))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "File content does not match extension header signature.");
        }

        // Sanitize and length-limit filename metadata
        var sanitizedName = Regex.Replace(rawName, @"[^\w\.\-]", "_");
        if (sanitizedName.Length > 200)
        {
            sanitizedName = sanitizedName[..196] + extension;
        }

        var currentTenantId = tenantContext.TenantId!.Value;
        var fileId = Guid.NewGuid();
        // Server-generated key strictly scoped by tenant and item
        var storedKey = $"tenants/{currentTenantId}/{itemId}/{fileId}";

        var contentType = file.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
        {
            contentType = GetDefaultContentType(extension);
        }

        var uploadResult = await storage.UploadAsync(storedKey, stream, contentType, cancellationToken);

        var itemFile = new ItemFile
        {
            Id = fileId,
            ItemId = itemId,
            OriginalName = sanitizedName,
            StoredKey = storedKey,
            ContentType = contentType,
            Size = uploadResult.Size
        };

        db.ItemFiles.Add(itemFile);

        var audit = new AuditLog
        {
            UserId = GetCurrentUserId(),
            Action = "FILE_UPLOAD",
            EntityType = nameof(ItemFile),
            EntityId = itemFile.Id
        };
        db.AuditLogs.Add(audit);

        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(DownloadFile), new { id = itemFile.Id },
            new FileResponse(itemFile.Id, itemFile.ItemId, itemFile.OriginalName, itemFile.ContentType, itemFile.Size, itemFile.CreatedAt));
    }

    [HttpGet("api/items/{itemId:guid}/files")]
    public async Task<ActionResult<IReadOnlyList<FileResponse>>> ListFiles(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Item not found");
        }

        var files = await db.ItemFiles.AsNoTracking()
            .Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FileResponse(x.Id, x.ItemId, x.OriginalName, x.ContentType, x.Size, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(files);
    }

    [HttpGet("api/files/{id:guid}/download")]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken cancellationToken)
    {
        var itemFile = await db.ItemFiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (itemFile is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "File not found");
        }

        // Verify key prefix belongs to current tenant
        var currentTenantId = tenantContext.TenantId!.Value;
        if (!itemFile.StoredKey.StartsWith($"tenants/{currentTenantId}/", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
        }

        var download = await storage.DownloadAsync(itemFile.StoredKey, itemFile.OriginalName, cancellationToken);
        if (download is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "File content not found in storage");
        }

        var audit = new AuditLog
        {
            UserId = GetCurrentUserId(),
            Action = "FILE_DOWNLOAD",
            EntityType = nameof(ItemFile),
            EntityId = itemFile.Id
        };
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync(cancellationToken);

        // Images (.png, .jpg, .jpeg) can be rendered inline; non-images forced download as attachment
        var isImage = itemFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (!isImage)
        {
            Response.Headers.ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(itemFile.OriginalName)}\"";
        }

        return File(download.ContentStream, itemFile.ContentType, isImage ? null : itemFile.OriginalName);
    }

    [HttpDelete("api/files/{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken cancellationToken)
    {
        var itemFile = await db.ItemFiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (itemFile is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "File not found");
        }

        // Delete from storage
        await storage.DeleteAsync(itemFile.StoredKey, cancellationToken);

        var audit = new AuditLog
        {
            UserId = GetCurrentUserId(),
            Action = "FILE_DELETE",
            EntityType = nameof(ItemFile),
            EntityId = itemFile.Id
        };
        db.AuditLogs.Add(audit);

        db.ItemFiles.Remove(itemFile);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static bool IsValidMagicBytes(string extension, byte[] bytes, int length)
    {
        if (length < 3) return false;

        switch (extension.ToLowerInvariant())
        {
            case ".png":
                return length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            case ".jpg":
            case ".jpeg":
                return length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            case ".pdf":
                return length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46; // %PDF
            case ".csv":
            case ".txt":
                // Must be printable text / UTF-8
                for (var i = 0; i < length; i++)
                {
                    if (bytes[i] == 0) return false; // reject null bytes
                }
                return true;
            default:
                return false;
        }
    }

    private static string GetDefaultContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        _ => "text/plain"
    };

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
