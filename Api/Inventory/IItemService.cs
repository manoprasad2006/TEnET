namespace TenNet.Api.Inventory;

public interface IItemService
{
    Task<PagedResult<ItemResponse>> GetItemsAsync(
        string? search,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ItemResponse?> GetItemByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ItemResponse> CreateItemAsync(CreateItemRequest request, Guid userId, CancellationToken cancellationToken = default);

    Task<ItemResponse?> UpdateItemAsync(Guid id, UpdateItemRequest request, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
