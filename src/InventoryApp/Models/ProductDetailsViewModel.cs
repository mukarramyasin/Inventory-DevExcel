using InventoryApp.Services;

namespace InventoryApp.Models;

/// <summary>Product header, its ledger, and the form that appends to that ledger.</summary>
public class ProductDetailsViewModel
{
    public required ProductDetail Product { get; set; }

    public StockMovementInputViewModel NewMovement { get; set; } = new();
}
