using InventoryApp.Domain.Entities;
using InventoryApp.Domain.Exceptions;

namespace InventoryApp.Services;

/// <summary>
/// All inventory business logic: stock arithmetic, validation and the concurrency-safe
/// movement write. Every user-correctable failure is a <see cref="DomainException"/>.
/// </summary>
public interface IInventoryService
{
    /// <summary>One page of products, filtered in the database.</summary>
    Task<PagedResult<ProductStockRow>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>Counts for every summary card, across the whole catalogue.</summary>
    Task<InventoryKpis> GetKpisAsync(CancellationToken cancellationToken = default);

    /// <summary>Header, stock and full ledger; null when the id is unknown.</summary>
    Task<ProductDetail?> GetProductDetailAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Minimal stock projection for the JSON endpoint; null when the id is unknown.</summary>
    Task<ProductStockRow?> GetProductStockAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Current editable values; null when the id is unknown.</summary>
    Task<ProductWriteModel?> GetProductForEditAsync(int productId, CancellationToken cancellationToken = default);

    /// <exception cref="DuplicateSkuException">The SKU is already in use.</exception>
    Task<int> CreateProductAsync(ProductWriteModel product, CancellationToken cancellationToken = default);

    /// <exception cref="ProductNotFoundException">No product has this id.</exception>
    /// <exception cref="DuplicateSkuException">The SKU belongs to another product.</exception>
    Task UpdateProductAsync(int productId, ProductWriteModel product, CancellationToken cancellationToken = default);

    /// <summary>Soft-deactivates or reactivates. Products are never deleted.</summary>
    /// <exception cref="ProductNotFoundException">No product has this id.</exception>
    Task SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends to the ledger. Never lets stock fall below zero, even when two requests
    /// arrive at the same instant.
    /// </summary>
    /// <exception cref="ProductNotFoundException">No product has this id.</exception>
    /// <exception cref="InactiveProductException">The product is deactivated.</exception>
    /// <exception cref="InvalidQuantityException">Quantity is not positive.</exception>
    /// <exception cref="InsufficientStockException">An Out movement exceeds current stock.</exception>
    /// <exception cref="ConcurrencyConflictException">Lost the concurrency race repeatedly.</exception>
    Task RecordMovementAsync(int productId, MovementType type, int quantity, string? note, CancellationToken cancellationToken = default);
}
