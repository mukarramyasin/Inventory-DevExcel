using InventoryApp.Domain.Exceptions;
using InventoryApp.Models;
using InventoryApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryApp.Controllers;

/// <summary>
/// Binds and validates input, calls <see cref="IInventoryService"/>, returns a view or a
/// redirect. No stock arithmetic and no EF Core calls here.
/// </summary>
public class ProductsController(IInventoryService inventory) : Controller
{
    private const string SuccessKey = "SuccessMessage";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, ProductFilter filter = ProductFilter.All, int page = 1, CancellationToken cancellationToken = default) =>
        View(await BuildListAsync(search, filter, page, cancellationToken));

    /// <summary>The same list as a fragment. Every AJAX interaction comes back through here.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? search, ProductFilter filter = ProductFilter.All, int page = 1, CancellationToken cancellationToken = default) =>
        PartialView("_ProductsPanel", await BuildListAsync(search, filter, page, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var product = await inventory.GetProductDetailAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return View(new ProductDetailsViewModel
        {
            Product = product,
            NewMovement = new StockMovementInputViewModel { ProductId = id }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordMovement(StockMovementInputViewModel newMovement, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return await RedisplayDetailsAsync(newMovement, cancellationToken);
        }

        try
        {
            await inventory.RecordMovementAsync(newMovement.ProductId, newMovement.Type, newMovement.Quantity, newMovement.Note, cancellationToken);
            TempData[SuccessKey] = $"{newMovement.Type.ToString().ToUpperInvariant()} movement of {newMovement.Quantity} recorded.";

            return RedirectToAction(nameof(Details), new { id = newMovement.ProductId });
        }
        catch (ProductNotFoundException)
        {
            return NotFound();
        }
        catch (DomainException ex)
        {
            // Insufficient stock, inactive product, bad quantity or a lost race: all
            // expected, all shown as a validation message on the same screen.
            ModelState.AddModelError(string.Empty, ex.Message);

            return await RedisplayDetailsAsync(newMovement, cancellationToken);
        }
    }

    [HttpGet]
    public IActionResult Create() => View(new ProductFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel form, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            var id = await inventory.CreateProductAsync(form.ToWriteModel(), cancellationToken);
            TempData[SuccessKey] = $"Product {form.Sku.Trim().ToUpperInvariant()} created.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (DuplicateSkuException ex)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Sku), ex.Message);

            return View(form);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var product = await inventory.GetProductForEditAsync(id, cancellationToken);

        return product is null ? NotFound() : View(ProductFormViewModel.FromWriteModel(id, product));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel form, CancellationToken cancellationToken = default)
    {
        form.Id = id;

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            await inventory.UpdateProductAsync(id, form.ToWriteModel(), cancellationToken);
            TempData[SuccessKey] = "Product updated.";

            return RedirectToAction(nameof(Details), new { id });
        }
        catch (ProductNotFoundException)
        {
            return NotFound();
        }
        catch (DuplicateSkuException ex)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Sku), ex.Message);

            return View(form);
        }
    }

    /// <summary>Answers with JSON; the caller re-requests the list, since counts move too.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool activate, CancellationToken cancellationToken = default)
    {
        try
        {
            await inventory.SetActiveAsync(id, activate, cancellationToken);
        }
        catch (ProductNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }

        return Ok(new { message = activate ? "Product reactivated." : "Product deactivated." });
    }

    [HttpGet]
    public async Task<IActionResult> ExportMovements(int id, CancellationToken cancellationToken = default)
    {
        var product = await inventory.GetProductDetailAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return File(InventoryCsvExporter.MovementsToCsv(product), "text/csv", InventoryCsvExporter.MovementsFileName(product));
    }

    [HttpGet]
    public async Task<IActionResult> ExportProducts(string? search, ProductFilter filter = ProductFilter.All, CancellationToken cancellationToken = default)
    {
        // Every matching row, not just the page on screen.
        var products = await inventory.GetProductsAsync(new ProductQuery(search, filter, 1, int.MaxValue), cancellationToken);

        return File(InventoryCsvExporter.ProductsToCsv(products.Items), "text/csv", InventoryCsvExporter.ProductsFileName());
    }

    /// <summary>Clamps a page number that has fallen past the end after a filter change.</summary>
    private async Task<ProductListViewModel> BuildListAsync(string? search, ProductFilter filter, int page, CancellationToken cancellationToken)
    {
        var query = new ProductQuery(search, filter, page, ProductQuery.DefaultPageSize);
        var result = await inventory.GetProductsAsync(query, cancellationToken);

        if (result.Items.Count == 0 && result.TotalCount > 0)
        {
            result = await inventory.GetProductsAsync(query with { Page = result.TotalPages }, cancellationToken);
        }

        return new ProductListViewModel
        {
            Search = search,
            Filter = filter,
            Kpis = await inventory.GetKpisAsync(cancellationToken),
            Page = result
        };
    }

    /// <summary>Re-renders details with the input intact, so a rejection loses nothing typed.</summary>
    private async Task<IActionResult> RedisplayDetailsAsync(StockMovementInputViewModel newMovement, CancellationToken cancellationToken)
    {
        var product = await inventory.GetProductDetailAsync(newMovement.ProductId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        return View(nameof(Details), new ProductDetailsViewModel { Product = product, NewMovement = newMovement });
    }
}
