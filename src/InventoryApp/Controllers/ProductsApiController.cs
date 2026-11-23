using InventoryApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryApp.Controllers;

/// <summary>Read-only JSON surface, kept separate so view and API concerns do not share a class.</summary>
[ApiController]
[Route("api/products")]
public class ProductsApiController(IInventoryService inventory) : ControllerBase
{
    /// <summary>GET /api/products/{id}/stock</summary>
    [HttpGet("{id:int}/stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStock(int id, CancellationToken cancellationToken = default)
    {
        var product = await inventory.GetProductStockAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = $"Product {id} was not found." });
        }

        return Ok(new
        {
            productId = product.Id,
            sku = product.Sku,
            name = product.Name,
            currentStock = product.CurrentStock,
            reorderLevel = product.ReorderLevel,
            isLowStock = product.IsLowStock
        });
    }
}
