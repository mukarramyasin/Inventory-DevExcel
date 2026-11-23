namespace InventoryApp.Domain.Entities;

/// <summary>
/// One immutable ledger entry. Movements are never edited or deleted; a mistake is
/// corrected by recording an opposing movement.
/// </summary>
public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public MovementType Type { get; set; }

    /// <summary>Always greater than zero; direction comes from <see cref="Type"/>.</summary>
    public int Quantity { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public Product? Product { get; set; }
}
