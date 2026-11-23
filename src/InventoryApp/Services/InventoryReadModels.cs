using InventoryApp.Domain.Entities;

namespace InventoryApp.Services;

public enum StockStatus { InStock, LowStock, OutOfStock }

/// <summary>The five buckets a product falls into. Doubles as the list filter.</summary>
public enum ProductFilter { All, Active, Inactive, LowStock, OutOfStock }

/// <summary>Single home for the status rule, so badge and CSV can never disagree.</summary>
public static class StockStatusEvaluator
{
    public static StockStatus Resolve(int currentStock, int reorderLevel) =>
        currentStock <= 0 ? StockStatus.OutOfStock
        : currentStock <= reorderLevel ? StockStatus.LowStock
        : StockStatus.InStock;

    public static string Label(StockStatus status) => status switch
    {
        StockStatus.OutOfStock => "Out of stock",
        StockStatus.LowStock => "Low stock",
        _ => "In stock"
    };
}

/// <summary>One list row, with stock already aggregated by the database.</summary>
public sealed record ProductStockRow(int Id, string Sku, string Name, int ReorderLevel, bool IsActive, int CurrentStock)
{
    public bool IsOutOfStock => CurrentStock <= 0;

    public bool IsLowStock => CurrentStock <= ReorderLevel;

    public StockStatus Status => StockStatusEvaluator.Resolve(CurrentStock, ReorderLevel);
}

/// <summary>One ledger entry as shown in the movement history.</summary>
public sealed record MovementRow(int Id, MovementType Type, int Quantity, string? Note, DateTime CreatedUtc);

/// <summary>Product header plus its ledger, for the details screen and CSV export.</summary>
public sealed record ProductDetail(
    int Id, string Sku, string Name, string? Description, int ReorderLevel,
    bool IsActive, DateTime CreatedUtc, int CurrentStock, List<MovementRow> Movements)
{
    public StockStatus Status => StockStatusEvaluator.Resolve(CurrentStock, ReorderLevel);
}

/// <summary>Counts behind the summary cards, one per <see cref="ProductFilter"/>.</summary>
public sealed record InventoryKpis(int All, int Active, int Inactive, int LowStock, int OutOfStock);

/// <summary>Everything needed to render one page of the list.</summary>
public sealed record ProductQuery(string? Search, ProductFilter Filter, int Page, int PageSize)
{
    public const int DefaultPageSize = 10;

    public static ProductQuery Default => new(null, ProductFilter.All, 1, DefaultPageSize);
}

/// <summary>One page of results, plus what a pager needs to render itself.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public int FirstRowNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastRowNumber => Math.Min(Page * PageSize, TotalCount);
}

/// <summary>Write-side payload for creating or editing a product.</summary>
public sealed record ProductWriteModel(string Sku, string Name, string? Description, int ReorderLevel, bool IsActive);
