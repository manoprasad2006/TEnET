using Microsoft.EntityFrameworkCore;
using TenNet.Data;
using TenNet.Domain;

namespace TenNet.Api.Inventory;

public sealed class DuplicateSkuException(string sku) : Exception($"SKU '{sku}' already exists in this tenant.")
{
    public string Sku { get; } = sku;
}

public sealed class ItemService(AppDbContext db) : IItemService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<ItemResponse>> GetItemsAsync(
        string? search,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Clamp(pageSize, 1, MaxPageSize);

        IQueryable<InventoryItem> query = db.InventoryItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmedSearch = search.Trim();
            query = query.Where(x => x.Name.Contains(trimmedSearch) || x.Sku.Contains(trimmedSearch));
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "sku" => sortDescending ? query.OrderByDescending(x => x.Sku) : query.OrderBy(x => x.Sku),
            "quantity" => sortDescending ? query.OrderByDescending(x => x.Quantity) : query.OrderBy(x => x.Quantity),
            "price" => sortDescending ? query.OrderByDescending(x => (double)x.Price) : query.OrderBy(x => (double)x.Price),
            "createdat" => sortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            "updatedat" => sortDescending ? query.OrderByDescending(x => x.UpdatedAt) : query.OrderBy(x => x.UpdatedAt),
            _ => sortDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ItemResponse(
                x.Id,
                x.Name,
                x.Sku,
                x.Quantity,
                x.Price,
                x.CreatedAt,
                x.UpdatedAt,
                x.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedResult<ItemResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<ItemResponse?> GetItemByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.InventoryItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null ? null : ToResponse(item);
    }

    public async Task<ItemResponse> CreateItemAsync(CreateItemRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var normalizedSku = request.Sku.Trim();
        var skuExists = await db.InventoryItems.AnyAsync(x => x.Sku == normalizedSku, cancellationToken);
        if (skuExists)
        {
            throw new DuplicateSkuException(normalizedSku);
        }

        var item = new InventoryItem
        {
            Name = request.Name.Trim(),
            Sku = normalizedSku,
            Quantity = request.Quantity,
            Price = decimal.Round(request.Price, 2)
        };

        db.InventoryItems.Add(item);

        var audit = new AuditLog
        {
            UserId = userId,
            Action = "CREATE",
            EntityType = nameof(InventoryItem),
            EntityId = item.Id
        };
        db.AuditLogs.Add(audit);

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(item);
    }

    public async Task<ItemResponse?> UpdateItemAsync(Guid id, UpdateItemRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var normalizedSku = request.Sku.Trim();
        var skuExists = await db.InventoryItems.AnyAsync(x => x.Id != id && x.Sku == normalizedSku, cancellationToken);
        if (skuExists)
        {
            throw new DuplicateSkuException(normalizedSku);
        }

        if (item.RowVersion != request.RowVersion)
        {
            throw new DbUpdateConcurrencyException("The item was modified by another operation.");
        }

        item.Name = request.Name.Trim();
        item.Sku = normalizedSku;
        item.Quantity = request.Quantity;
        item.Price = decimal.Round(request.Price, 2);

        var audit = new AuditLog
        {
            UserId = userId,
            Action = "UPDATE",
            EntityType = nameof(InventoryItem),
            EntityId = item.Id
        };
        db.AuditLogs.Add(audit);

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(item);
    }

    public async Task<bool> DeleteItemAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var audit = new AuditLog
        {
            UserId = userId,
            Action = "DELETE",
            EntityType = nameof(InventoryItem),
            EntityId = item.Id
        };
        db.AuditLogs.Add(audit);

        db.InventoryItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ItemResponse ToResponse(InventoryItem item) =>
        new(item.Id, item.Name, item.Sku, item.Quantity, item.Price, item.CreatedAt, item.UpdatedAt, item.RowVersion);
}
