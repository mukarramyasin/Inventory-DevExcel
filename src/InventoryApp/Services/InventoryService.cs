using InventoryApp.Data;
using InventoryApp.Domain.Entities;
using InventoryApp.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Services;

/// <inheritdoc cref="IInventoryService"/>
public sealed class InventoryService(AppDbContext db, ILogger<InventoryService> logger) : IInventoryService
{
    /// <summary>Retries after a lost concurrency check. Each one re-reads the real stock.</summary>
    private const int MaxConcurrencyAttempts = 4;

    public async Task<PagedResult<ProductStockRow>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? ProductQuery.DefaultPageSize : query.PageSize;

        // One intermediate projection, reused by the search, the filter, the COUNT and the
        // page fetch. Exactly two statements run, and no movement rows are materialised.
        var rows = db.Products.AsNoTracking()
            .Select(p => new
            {
                Product = p,
                CurrentStock = p.Movements.Sum(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity)
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Upper-cased on both sides, so the match never depends on column collation.
            var term = query.Search.Trim().ToUpper();
            rows = rows.Where(r => r.Product.Sku.ToUpper().Contains(term) || r.Product.Name.ToUpper().Contains(term));
        }

        rows = query.Filter switch
        {
            ProductFilter.Active => rows.Where(r => r.Product.IsActive),
            ProductFilter.Inactive => rows.Where(r => !r.Product.IsActive),
            ProductFilter.LowStock => rows.Where(r => r.Product.IsActive && r.CurrentStock > 0 && r.CurrentStock <= r.Product.ReorderLevel),
            ProductFilter.OutOfStock => rows.Where(r => r.Product.IsActive && r.CurrentStock <= 0),
            _ => rows
        };

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(r => r.Product.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ProductStockRow(r.Product.Id, r.Product.Sku, r.Product.Name, r.Product.ReorderLevel, r.Product.IsActive, r.CurrentStock))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductStockRow>(items, page, pageSize, totalCount);
    }

    public async Task<InventoryKpis> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        // One projected query; the counts are derived in memory. Beyond a few thousand
        // products this should become a database-side aggregate or an indexed view.
        var snapshots = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.IsActive,
                p.ReorderLevel,
                CurrentStock = p.Movements.Sum(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity)
            })
            .ToListAsync(cancellationToken);

        var active = snapshots.Count(s => s.IsActive);

        return new InventoryKpis(
            All: snapshots.Count,
            Active: active,
            Inactive: snapshots.Count - active,
            LowStock: snapshots.Count(s => s.IsActive && s.CurrentStock > 0 && s.CurrentStock <= s.ReorderLevel),
            OutOfStock: snapshots.Count(s => s.IsActive && s.CurrentStock <= 0));
    }

    public async Task<ProductDetail?> GetProductDetailAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductDetail(
                p.Id, p.Sku, p.Name, p.Description, p.ReorderLevel, p.IsActive, p.CreatedUtc,
                p.Movements.Sum(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity),
                // Newest first; Id breaks ties on equal timestamps.
                p.Movements.OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id)
                    .Select(m => new MovementRow(m.Id, m.Type, m.Quantity, m.Note, m.CreatedUtc)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ProductStockRow?> GetProductStockAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductStockRow(
                p.Id, p.Sku, p.Name, p.ReorderLevel, p.IsActive,
                p.Movements.Sum(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ProductWriteModel?> GetProductForEditAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductWriteModel(p.Sku, p.Name, p.Description, p.ReorderLevel, p.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CreateProductAsync(ProductWriteModel product, CancellationToken cancellationToken = default)
    {
        var sku = NormaliseSku(product.Sku);
        var name = RequireName(product.Name);
        GuardReorderLevel(product.ReorderLevel);

        // Pre-flight check, so the common case gets a friendly field-level message.
        if (await db.Products.AsNoTracking().AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new DuplicateSkuException(sku);
        }

        var entity = new Product
        {
            Sku = sku,
            Name = name,
            Description = NormaliseOptional(product.Description),
            ReorderLevel = product.ReorderLevel,
            IsActive = product.IsActive,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow
        };

        db.Products.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Two creates raced past the pre-flight check; the unique index settled it.
            db.ChangeTracker.Clear();
            throw new DuplicateSkuException(sku);
        }

        return entity.Id;
    }

    public async Task UpdateProductAsync(int productId, ProductWriteModel product, CancellationToken cancellationToken = default)
    {
        var sku = NormaliseSku(product.Sku);
        var name = RequireName(product.Name);
        GuardReorderLevel(product.ReorderLevel);

        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                     ?? throw new ProductNotFoundException(productId);

        if (await db.Products.AsNoTracking().AnyAsync(p => p.Sku == sku && p.Id != productId, cancellationToken))
        {
            throw new DuplicateSkuException(sku);
        }

        entity.Sku = sku;
        entity.Name = name;
        entity.Description = NormaliseOptional(product.Description);
        entity.ReorderLevel = product.ReorderLevel;
        entity.IsActive = product.IsActive;
        entity.ConcurrencyToken = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            db.ChangeTracker.Clear();
            throw new DuplicateSkuException(sku);
        }
    }

    public async Task SetActiveAsync(int productId, bool isActive, CancellationToken cancellationToken = default)
    {
        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                     ?? throw new ProductNotFoundException(productId);

        if (entity.IsActive == isActive)
        {
            return;
        }

        entity.IsActive = isActive;
        entity.ConcurrencyToken = Guid.NewGuid();

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordMovementAsync(int productId, MovementType type, int quantity, string? note, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException(quantity);
        }

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                await WriteMovementAsync(productId, type, quantity, note, cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone committed between our read and our write, so the stock we
                // validated against is stale. Drop the cached entity and start over: the
                // retry either succeeds or fails correctly with InsufficientStockException.
                db.ChangeTracker.Clear();

                if (attempt == MaxConcurrencyAttempts)
                {
                    logger.LogWarning("Gave up recording a {MovementType} of {Quantity} for product {ProductId} after {Attempts} conflicts.",
                        type, quantity, productId, attempt);

                    throw new ConcurrencyConflictException();
                }

                logger.LogInformation("Concurrency conflict on product {ProductId}, retrying (attempt {Attempt} of {MaxAttempts}).",
                    productId, attempt, MaxConcurrencyAttempts);
            }
        }
    }

    /// <summary>
    /// One attempt at the write, and the reason stock can never go negative.
    /// <para>
    /// The transaction makes the stock check and the insert atomic. That alone is not
    /// enough: two readers can both see stock 8 and both think issuing 8 is fine. So the
    /// write also re-stamps the parent product's <c>ConcurrencyToken</c>, which turns it
    /// into <c>UPDATE ... WHERE Id = @id AND ConcurrencyToken = @original</c>. The first
    /// writer moves the token; the second matches zero rows, EF throws, and the
    /// transaction rolls back. Every movement for a product funnels through that one row.
    /// </para>
    /// </summary>
    private async Task WriteMovementAsync(int productId, MovementType type, int quantity, string? note, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Tracked on purpose: EF needs the token it read for the UPDATE's WHERE clause.
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                      ?? throw new ProductNotFoundException(productId);

        if (!product.IsActive)
        {
            throw new InactiveProductException(product.Id, product.Sku);
        }

        if (type == MovementType.Out)
        {
            var currentStock = await GetCurrentStockAsync(productId, cancellationToken);

            if (quantity > currentStock)
            {
                throw new InsufficientStockException(quantity, currentStock);
            }
        }

        db.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            Type = type,
            Quantity = quantity,
            Note = NormaliseOptional(note),
            CreatedUtc = DateTime.UtcNow
        });

        product.ConcurrencyToken = Guid.NewGuid();

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Stock straight from the ledger as a single SUM with a CASE.</summary>
    private async Task<int> GetCurrentStockAsync(int productId, CancellationToken cancellationToken) =>
        await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .SumAsync(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity, cancellationToken);

    private static string NormaliseSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return sku.Trim().ToUpperInvariant();
    }

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    private static string? NormaliseOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Defence in depth; the ViewModel's [Range] is the user-facing gate.</summary>
    private static void GuardReorderLevel(int reorderLevel)
    {
        if (reorderLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reorderLevel), reorderLevel, "Reorder level cannot be negative.");
        }
    }

    /// <summary>SQL Server: 2601 duplicate key in a unique index, 2627 unique constraint.</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
