using InventoryApp.Domain.Entities;
using InventoryApp.Domain.Exceptions;
using InventoryApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryApp.Tests;

/// <summary>
/// Behaviour tests for <see cref="InventoryService"/> against a real SQL Server database.
/// Each test owns its own database, so nothing leaks between them.
/// </summary>
public class InventoryServiceTests
{
    [Fact]
    public async Task CurrentStock_IsSumOfInMovementsMinusOutMovements()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        var productId = await service.CreateProductAsync(new ProductWriteModel("STK-001", "Widget", null, 5, true));

        await service.RecordMovementAsync(productId, MovementType.In, 50, "opening");
        await service.RecordMovementAsync(productId, MovementType.Out, 12, "job A");
        await service.RecordMovementAsync(productId, MovementType.In, 5, "return");
        await service.RecordMovementAsync(productId, MovementType.Out, 8, "job B");

        var detail = await service.GetProductDetailAsync(productId);

        Assert.NotNull(detail);
        Assert.Equal(35, detail!.CurrentStock);
        Assert.Equal(4, detail.Movements.Count);

        // The ledger is shown newest first.
        Assert.Equal(MovementType.Out, detail.Movements[0].Type);
        Assert.Equal(8, detail.Movements[0].Quantity);
    }

    [Fact]
    public async Task OutMovement_ThatExceedsStock_IsRejected_AndReportsAvailableQuantity()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        var productId = await service.CreateProductAsync(new ProductWriteModel("STK-002", "Oil filter", null, 25, true));
        await service.RecordMovementAsync(productId, MovementType.In, 40, "opening");
        await service.RecordMovementAsync(productId, MovementType.Out, 32, "bulk issue");

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(
            () => service.RecordMovementAsync(productId, MovementType.Out, 15, "too many"));

        Assert.Equal(15, exception.Requested);
        Assert.Equal(8, exception.Available);
        Assert.Equal("Cannot issue 15 units. Only 8 units are currently in stock.", exception.Message);

        // The rejected movement must leave no trace.
        var detail = await service.GetProductDetailAsync(productId);
        Assert.Equal(8, detail!.CurrentStock);
        Assert.Equal(2, detail.Movements.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Movement_WithNonPositiveQuantity_IsRejected(int quantity)
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        var productId = await service.CreateProductAsync(new ProductWriteModel("STK-003", "Spark plug", null, 0, true));
        await service.RecordMovementAsync(productId, MovementType.In, 10, "opening");

        await Assert.ThrowsAsync<InvalidQuantityException>(
            () => service.RecordMovementAsync(productId, MovementType.In, quantity, null));

        await Assert.ThrowsAsync<InvalidQuantityException>(
            () => service.RecordMovementAsync(productId, MovementType.Out, quantity, null));

        var detail = await service.GetProductDetailAsync(productId);
        Assert.Equal(10, detail!.CurrentStock);
    }

    [Fact]
    public async Task CreateProduct_WithDuplicateSku_IsRejected_IgnoringCaseAndSurroundingSpace()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        await service.CreateProductAsync(new ProductWriteModel("DUP-001", "First", null, 0, true));

        var exception = await Assert.ThrowsAsync<DuplicateSkuException>(
            () => service.CreateProductAsync(new ProductWriteModel("  dup-001  ", "Second", null, 0, true)));

        Assert.Equal("DUP-001", exception.Sku);

        var products = await service.GetProductsAsync(new ProductQuery(null, ProductFilter.All, 1, 50));
        Assert.Single(products.Items);
    }

    [Fact]
    public async Task UpdateProduct_CannotTakeAnotherProductsSku()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        await service.CreateProductAsync(new ProductWriteModel("SKU-A", "A", null, 0, true));
        var secondId = await service.CreateProductAsync(new ProductWriteModel("SKU-B", "B", null, 0, true));

        await Assert.ThrowsAsync<DuplicateSkuException>(
            () => service.UpdateProductAsync(secondId, new ProductWriteModel("sku-a", "B renamed", null, 0, true)));

        // Editing a product while keeping its own SKU must still be allowed.
        await service.UpdateProductAsync(secondId, new ProductWriteModel("sku-b", "B renamed", null, 3, true));

        var detail = await service.GetProductDetailAsync(secondId);
        Assert.Equal("SKU-B", detail!.Sku);
        Assert.Equal("B renamed", detail.Name);
        Assert.Equal(3, detail.ReorderLevel);
    }

    [Fact]
    public async Task Deactivation_PreservesHistory_BlocksNewMovements_AndIsReversible()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        var productId = await service.CreateProductAsync(new ProductWriteModel("SOFT-001", "Clutch kit", null, 2, true));
        await service.RecordMovementAsync(productId, MovementType.In, 10, "opening");

        await service.SetActiveAsync(productId, isActive: false);

        var deactivated = await service.GetProductDetailAsync(productId);
        Assert.False(deactivated!.IsActive);
        Assert.Single(deactivated.Movements);
        Assert.Equal(10, deactivated.CurrentStock);

        await Assert.ThrowsAsync<InactiveProductException>(
            () => service.RecordMovementAsync(productId, MovementType.In, 1, "blocked"));

        // Filtered out of the Active bucket, still reachable through Inactive and All.
        Assert.Empty((await service.GetProductsAsync(new ProductQuery(null, ProductFilter.Active, 1, 50))).Items);
        Assert.Single((await service.GetProductsAsync(new ProductQuery(null, ProductFilter.Inactive, 1, 50))).Items);
        Assert.Single((await service.GetProductsAsync(new ProductQuery(null, ProductFilter.All, 1, 50))).Items);

        await service.SetActiveAsync(productId, isActive: true);
        await service.RecordMovementAsync(productId, MovementType.In, 5, "after reactivation");

        var reactivated = await service.GetProductDetailAsync(productId);
        Assert.Equal(15, reactivated!.CurrentStock);
    }

    [Fact]
    public async Task ProductList_PagesServerSide_AndReportsTheTotal()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        for (var i = 1; i <= 12; i++)
        {
            await service.CreateProductAsync(
                new ProductWriteModel($"PAGE-{i:D3}", $"Product {i}", null, 0, true));
        }

        var first = await service.GetProductsAsync(new ProductQuery(null, ProductFilter.All, 1, 10));

        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(10, first.Items.Count);
        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);
        Assert.Equal("PAGE-001", first.Items[0].Sku);

        var second = await service.GetProductsAsync(new ProductQuery(null, ProductFilter.All, 2, 10));

        Assert.Equal(2, second.Items.Count);
        Assert.Equal("PAGE-011", second.Items[0].Sku);
        Assert.True(second.HasPrevious);
        Assert.False(second.HasNext);

        // No overlap between the pages: the ordering is stable across requests.
        Assert.Empty(first.Items.Select(r => r.Id).Intersect(second.Items.Select(r => r.Id)));
    }

    [Fact]
    public async Task ProductList_BucketFilters_AgreeWithTheSummaryCounts()
    {
        using var database = new SqlServerTestDatabase();
        await using var context = database.CreateContext();
        var service = database.CreateService(context);

        var healthy = await service.CreateProductAsync(new ProductWriteModel("BUCKET-OK", "Healthy", null, 5, true));
        await service.RecordMovementAsync(healthy, MovementType.In, 50, null);

        var low = await service.CreateProductAsync(new ProductWriteModel("BUCKET-LOW", "Running low", null, 25, true));
        await service.RecordMovementAsync(low, MovementType.In, 8, null);

        await service.CreateProductAsync(new ProductWriteModel("BUCKET-ZERO", "Nothing left", null, 4, true));

        var retired = await service.CreateProductAsync(new ProductWriteModel("BUCKET-OFF", "Retired", null, 2, true));
        await service.SetActiveAsync(retired, isActive: false);

        var kpis = await service.GetKpisAsync();

        Assert.Equal(4, kpis.All);
        Assert.Equal(3, kpis.Active);
        Assert.Equal(1, kpis.Inactive);
        Assert.Equal(1, kpis.LowStock);
        Assert.Equal(1, kpis.OutOfStock);

        // Each card's count must match the number of rows its filter actually returns.
        await AssertBucketAsync(ProductFilter.All, kpis.All);
        await AssertBucketAsync(ProductFilter.Active, kpis.Active);
        await AssertBucketAsync(ProductFilter.Inactive, kpis.Inactive);
        await AssertBucketAsync(ProductFilter.LowStock, kpis.LowStock);
        await AssertBucketAsync(ProductFilter.OutOfStock, kpis.OutOfStock);

        async Task AssertBucketAsync(ProductFilter filter, int expected)
        {
            var result = await service.GetProductsAsync(new ProductQuery(null, filter, 1, 50));
            Assert.Equal(expected, result.TotalCount);
        }
    }

    [Fact]
    public async Task ConcurrencyToken_MakesTheSecondWriterFail_WithDbUpdateConcurrencyException()
    {
        using var database = new SqlServerTestDatabase();

        int productId;
        await using (var seed = database.CreateContext())
        {
            productId = await database.CreateService(seed).CreateProductAsync(
                new ProductWriteModel("TOKEN-001", "Contested item", null, 0, true));
        }

        // Two contexts standing in for two web requests that both read the product before
        // either of them wrote - the exact situation the token exists to catch. No threads
        // and no timing: the race is staged by hand, so the result is deterministic.
        await using var firstRequest = database.CreateContext();
        await using var secondRequest = database.CreateContext();

        var firstCopy = await firstRequest.Products.FirstAsync(p => p.Id == productId);
        var secondCopy = await secondRequest.Products.FirstAsync(p => p.Id == productId);

        Assert.Equal(firstCopy.ConcurrencyToken, secondCopy.ConcurrencyToken);

        // The first writer commits and moves the token on.
        firstCopy.ConcurrencyToken = Guid.NewGuid();
        await firstRequest.SaveChangesAsync();

        // The second writer still carries the original value, so its UPDATE matches zero
        // rows. Without [ConcurrencyCheck] on the property this write would silently
        // succeed and overwrite the first - which is how a lost update happens.
        secondCopy.ConcurrencyToken = Guid.NewGuid();

        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondRequest.SaveChangesAsync());

        var conflicted = Assert.Single(conflict.Entries);
        Assert.IsType<Product>(conflicted.Entity);

        // The winner's value is what survived in the database.
        await using var verify = database.CreateContext();
        var stored = await verify.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        Assert.Equal(firstCopy.ConcurrencyToken, stored.ConcurrencyToken);
    }

    [Fact]
    public async Task ConcurrentOutMovements_CanNeverDriveStockNegative()
    {
        using var database = new SqlServerTestDatabase();

        const int openingStock = 10;
        const int quantityPerRequest = 4;
        const int parallelRequests = 8;
        const int maxLegitimateSuccesses = openingStock / quantityPerRequest; // 2

        int productId;
        await using (var seedContext = database.CreateContext())
        {
            var seedService = database.CreateService(seedContext);
            productId = await seedService.CreateProductAsync(new ProductWriteModel("RACE-001", "Contested item", null, 0, true));
            await seedService.RecordMovementAsync(productId, MovementType.In, openingStock, "opening");
        }

        // Each task gets its own context and service, exactly as each web request would,
        // and they are all released at once against the same database.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, parallelRequests).Select(async _ =>
        {
            await using var context = database.CreateContext();
            var service = database.CreateService(context);

            try
            {
                await service.RecordMovementAsync(productId, MovementType.Out, quantityPerRequest, "parallel issue");
                return (Succeeded: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Succeeded: false, Error: ex);
            }
        }));

        var successes = outcomes.Count(o => o.Succeeded);

        await using var verifyContext = database.CreateContext();
        var finalStock = await database.CreateService(verifyContext).GetProductDetailAsync(productId);

        Assert.NotNull(finalStock);

        // The guarantee under test: the ledger can never be driven below zero.
        Assert.True(finalStock!.CurrentStock >= 0, $"Stock went negative: {finalStock.CurrentStock}.");

        // Only whole, affordable issues may have landed, and the ledger must agree exactly
        // with how many of them did.
        Assert.InRange(successes, 1, maxLegitimateSuccesses);
        Assert.Equal(openingStock - (successes * quantityPerRequest), finalStock.CurrentStock);

        // Every loser failed for an expected business reason, never with a raw database or
        // infrastructure error.
        Assert.All(
            outcomes.Where(o => !o.Succeeded),
            outcome => Assert.IsAssignableFrom<DomainException>(outcome.Error!));
    }
}
