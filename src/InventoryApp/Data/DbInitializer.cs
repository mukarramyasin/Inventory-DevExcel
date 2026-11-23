using InventoryApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Data;

/// <summary>
/// Migrates on startup and seeds an opening catalogue. Idempotent: skipped once any
/// product exists. Stock is seeded as movements, never as a figure.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var opened = DateTime.UtcNow.AddDays(-30);

        // Covers every state the list can render: healthy, low, zero, and deactivated.
        db.Products.AddRange(
            NewProduct("BRK-PAD-001", "Brake Pad Set - Front Axle", "Ceramic pads, fits 2015-2022 sedans.", 10, true, opened,
                Movement(MovementType.In, 50, "Opening stock from supplier PO-1041", opened.AddDays(1)),
                Movement(MovementType.Out, 12, "Workshop job WJ-2210", opened.AddDays(6)),
                Movement(MovementType.Out, 8, "Workshop job WJ-2231", opened.AddDays(14))),

            NewProduct("OIL-FLT-010", "Oil Filter - Spin-on", "Standard spin-on filter, 3/4-16 thread.", 25, true, opened,
                Movement(MovementType.In, 40, "Opening stock from supplier PO-1041", opened.AddDays(1)),
                Movement(MovementType.Out, 32, "Bulk issue to service bay", opened.AddDays(18))),

            NewProduct("TYR-195-65", "Tyre 195/65 R15", "All-season passenger tyre.", 4, true, opened,
                Movement(MovementType.In, 16, "Opening stock from supplier PO-1052", opened.AddDays(3)),
                Movement(MovementType.Out, 16, "Fleet replacement contract FC-77", opened.AddDays(21))),

            NewProduct("WPR-BLD-022", "Wiper Blade 22 inch", null, 12, true, opened,
                Movement(MovementType.In, 60, "Opening stock from supplier PO-1052", opened.AddDays(3)),
                Movement(MovementType.Out, 15, "Counter sales, week 2", opened.AddDays(10)),
                Movement(MovementType.In, 10, "Customer return, resalable", opened.AddDays(16)),
                Movement(MovementType.Out, 20, "Counter sales, week 4", opened.AddDays(24))),

            NewProduct("SPK-PLG-004", "Spark Plug - Iridium", "Long-life iridium plug, gap 0.044 in.", 50, true, opened,
                Movement(MovementType.In, 120, "Opening stock from supplier PO-1063", opened.AddDays(5)),
                Movement(MovementType.Out, 90, "Tune-up campaign", opened.AddDays(22))),

            NewProduct("CLT-KIT-300", "Clutch Kit 300mm", "Discontinued line, kept for audit history.", 2, false, opened,
                Movement(MovementType.In, 6, "Opening stock from supplier PO-1063", opened.AddDays(5)),
                Movement(MovementType.Out, 6, "Cleared before line was discontinued", opened.AddDays(20))));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Product NewProduct(string sku, string name, string? description, int reorderLevel, bool isActive, DateTime createdUtc, params StockMovement[] movements) =>
        new()
        {
            Sku = sku,
            Name = name,
            Description = description,
            ReorderLevel = reorderLevel,
            IsActive = isActive,
            CreatedUtc = createdUtc,
            ConcurrencyToken = Guid.NewGuid(),
            Movements = movements.ToList()
        };

    private static StockMovement Movement(MovementType type, int quantity, string? note, DateTime createdUtc) =>
        new() { Type = type, Quantity = quantity, Note = note, CreatedUtc = createdUtc };
}
