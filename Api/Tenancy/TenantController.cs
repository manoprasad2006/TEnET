using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenNet.Data;
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

    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts([FromServices] AppDbContext db, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSet || tenantContext.TenantId is null)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Tenant context is not set.");
        }

        var itemCount = await db.InventoryItems.CountAsync(cancellationToken);
        var fileCount = await db.ItemFiles.CountAsync(cancellationToken);
        var auditCount = await db.AuditLogs.CountAsync(cancellationToken);

        return Ok(new
        {
            tenantId = tenantContext.TenantId.Value,
            tenantName = tenantContext.TenantName,
            items = itemCount,
            files = fileCount,
            audits = auditCount
        });
    }
}
