using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenNet.Domain;

namespace TenNet.Api.Inventory;

[ApiController]
[Route("api/items")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ItemsController(IItemService itemService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ItemResponse>>> GetItems(
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await itemService.GetItemsAsync(search, sortBy, sortDescending, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ItemResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await itemService.GetItemByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Item not found");
        }

        return Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<ItemResponse>> Create([FromBody] CreateItemRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        try
        {
            var item = await itemService.CreateItemAsync(request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
        }
        catch (DuplicateSkuException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message);
        }
        catch (TenantViolationException ex)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ItemResponse>> Update(Guid id, [FromBody] UpdateItemRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        try
        {
            var updated = await itemService.UpdateItemAsync(id, request, userId, cancellationToken);
            if (updated is null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Item not found");
            }

            return Ok(updated);
        }
        catch (DuplicateSkuException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: "The item has been modified by another request.");
        }
        catch (TenantViolationException ex)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        try
        {
            var deleted = await itemService.DeleteItemAsync(id, userId, cancellationToken);
            if (!deleted)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Item not found");
            }

            return NoContent();
        }
        catch (TenantViolationException ex)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: ex.Message);
        }
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
