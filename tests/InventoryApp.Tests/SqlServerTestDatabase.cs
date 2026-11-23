using InventoryApp.Data;
using InventoryApp.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InventoryApp.Tests;

/// <summary>
/// A disposable, uniquely named SQL Server database for a single test.
/// <para>
/// The tests deliberately run against a real SQL Server rather than the EF Core
/// in-memory provider, because the behaviour under test is relational: explicit
/// transactions, the unique SKU index, the <c>Quantity &gt; 0</c> check constraint and
/// row-level locking. The in-memory provider implements none of those, so a suite built
/// on it would pass while the real thing broke.
/// </para>
/// <para>
/// Each test gets its own database and drops it afterwards, so tests never see each
/// other's data. The concurrency test opens several connections to the same database,
/// exactly as several web requests would.
/// </para>
/// </summary>
public sealed class SqlServerTestDatabase : IDisposable
{
    /// <summary>
    /// Server used by the tests. Defaults to the LocalDB instance that ships with Visual
    /// Studio; override with the INVENTORYAPP_TEST_SERVER environment variable to point
    /// at a different instance, for example a CI server or "DESKTOP-5LAQO8V".
    /// </summary>
    private static string Server =>
        Environment.GetEnvironmentVariable("INVENTORYAPP_TEST_SERVER") is { Length: > 0 } configured
            ? configured
            : @"(localdb)\MSSQLLocalDB";

    public SqlServerTestDatabase()
    {
        DatabaseName = $"InventoryApp_Test_{Guid.NewGuid():N}";
        ConnectionString =
            $"Server={Server};Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";

        // Migrate rather than EnsureCreated, so every test runs against the same schema
        // the application ships - a broken migration fails here instead of in production.
        using var context = CreateContext();
        context.Database.Migrate();
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    /// <summary>A fresh context, standing in for one web request's scoped context.</summary>
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    public IInventoryService CreateService(AppDbContext context) =>
        new InventoryService(context, NullLogger<InventoryService>.Instance);

    public void Dispose()
    {
        try
        {
            // Pooled connections would otherwise hold the database open and block the drop.
            SqlConnection.ClearAllPools();

            using var context = CreateContext();
            context.Database.EnsureDeleted();
        }
        catch (SqlException)
        {
            // Dropping is housekeeping, not an assertion: a database that cannot be
            // dropped right now must not turn a passing test red. Leftovers are all named
            // InventoryApp_Test_* and can be removed in bulk.
        }
    }
}
