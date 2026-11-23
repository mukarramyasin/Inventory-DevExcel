using System.Diagnostics;
using InventoryApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventoryApp.Controllers;

/// <summary>Error pages. Nothing here touches the database, so they render when it is the data layer that failed.</summary>
public class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(ProductsController.Index), "Products");

    /// <summary>Target of UseExceptionHandler. Shows a trace id, never the exception.</summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    /// <summary>Target of UseStatusCodePagesWithReExecute. Named Status so it does not hide ControllerBase.StatusCode.</summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int? code) =>
        code == StatusCodes.Status404NotFound
            ? View("NotFound")
            : View("Error", new ErrorViewModel { StatusCode = code, RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
