using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Domain.Entities;

/// <summary>
/// A stock-keeping item. Has no Stock column on purpose: current stock is always derived
/// from the <see cref="StockMovement"/> ledger, so it cannot drift from its own history.
/// </summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>Unique business key, stored trimmed and upper-cased.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Stock at or below this level is reported as low.</summary>
    public int ReorderLevel { get; set; }

    /// <summary>Soft-deactivation flag. Products are never deleted, so history survives.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optimistic-concurrency guard, re-stamped on every edit and every movement write.
    /// EF Core puts the value it read into the UPDATE's WHERE clause, so a second writer
    /// working from stale stock matches zero rows and is rejected. See the README.
    /// </summary>
    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
}
