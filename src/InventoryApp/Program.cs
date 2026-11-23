using InventoryApp.Data;
using InventoryApp.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException(
                           "Connection string 'DefaultConnection' is missing from configuration.");

// EnableRetryOnFailure is deliberately NOT configured. Its retrying execution strategy
// refuses user-initiated transactions, and InventoryService opens one explicitly for
// every movement write. Adding it would require wrapping that transaction in
// DbContext.Database.CreateExecutionStrategy().ExecuteAsync(...) first.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// Scoped: one DbContext and one service per request, which is what makes the
// per-request change tracking and transaction boundaries in InventoryService correct.
builder.Services.AddScoped<IInventoryService, InventoryService>();

var app = builder.Build();

// Registered in every environment, including Development. The developer exception page
// is deliberately not used: the requirement is that a stack trace never reaches the
// browser, and testing the real error page is the only way to know it works.
app.UseExceptionHandler("/Home/Error");

// Turns a bare 404 or any other error status into the friendly Razor page, by
// re-executing the pipeline against /Home/Status?code=404 while keeping the status code.
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// Product list is the default route, as required.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Products}/{action=Index}/{id?}");

// Attribute-routed JSON endpoint (/api/products/{id}/stock).
app.MapControllers();

// Migrate and seed before the first request is served, so a fresh clone or a deleted
// .db file both come up with a working, populated database.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        await DbInitializer.InitializeAsync(db);
    }
    catch (SqlException ex)
    {
        // A fresh clone on a machine without LocalDB fails here. Say why in one line,
        // rather than letting a raw connection stack trace be the first thing seen.
        app.Logger.LogCritical(ex,
            "Could not reach the database. This app expects SQL Server LocalDB, which ships with Visual Studio - "
            + "check it is present with 'sqllocaldb info', or point ConnectionStrings:DefaultConnection in "
            + "appsettings.json at another SQL Server instance.");

        throw;
    }
}

app.Run();
