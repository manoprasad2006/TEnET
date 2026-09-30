using System.ComponentModel.DataAnnotations;

namespace TenNet.Api.Inventory;

public sealed record CreateItemRequest(
    [Required, StringLength(120, MinimumLength = 1)]
    string Name,

    [Required, RegularExpression(@"^[A-Za-z0-9\-_]{1,40}$", ErrorMessage = "SKU must be 1-40 alphanumeric characters, hyphens or underscores.")]
    string Sku,

    [Range(0, 1_000_000, ErrorMessage = "Quantity must be between 0 and 1,000,000.")]
    int Quantity,

    [Range(0.00, 999_999_999.99, ErrorMessage = "Price must be non-negative.")]
    decimal Price
);

public sealed record UpdateItemRequest(
    [Required, StringLength(120, MinimumLength = 1)]
    string Name,

    [Required, RegularExpression(@"^[A-Za-z0-9\-_]{1,40}$", ErrorMessage = "SKU must be 1-40 alphanumeric characters, hyphens or underscores.")]
    string Sku,

    [Range(0, 1_000_000, ErrorMessage = "Quantity must be between 0 and 1,000,000.")]
    int Quantity,

    [Range(0.00, 999_999_999.99, ErrorMessage = "Price must be non-negative.")]
    decimal Price,

    [Required]
    Guid RowVersion
);

public sealed record ItemResponse(
    Guid Id,
    string Name,
    string Sku,
    int Quantity,
    decimal Price,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid RowVersion
);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);
