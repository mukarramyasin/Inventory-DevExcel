using InventoryApp.Services;

namespace InventoryApp.Models;

/// <summary>
/// Backs the product list. The same model renders the full page and the AJAX partial, so
/// an AJAX update is always what a full reload would have produced.
/// </summary>
public class ProductListViewModel
{
    public string? Search { get; set; }

    public ProductFilter Filter { get; set; } = ProductFilter.All;

    public InventoryKpis Kpis { get; set; } = new(0, 0, 0, 0, 0);

    public PagedResult<ProductStockRow> Page { get; set; } = new([], 1, ProductQuery.DefaultPageSize, 0);

    public bool HasFilter => !string.IsNullOrWhiteSpace(Search) || Filter != ProductFilter.All;
}
