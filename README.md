# Inventory & Stock Movements

A self-contained inventory module built with ASP.NET Core 10 MVC, EF Core 10 and SQL
Server. Products have **no stock column**: current stock is always derived from an
append-only movement ledger, and an `Out` movement can never drive stock below zero —
including when two requests arrive at the same instant.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should report `10.x`)
- SQL Server LocalDB (installed with Visual Studio; verify with `sqllocaldb info`)

Bootstrap 5 is loaded from a CDN, so there is no front-end build step.

## Configure

**Nothing to configure.** The default connection string targets SQL Server LocalDB, which
ships with Visual Studio, so a fresh clone runs unchanged:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=InventoryApp;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

To use a different instance, change `Server=` in `src/InventoryApp/appsettings.json`.
`TrustServerCertificate=True` is there because a local development instance normally uses
a self-signed certificate; in a real deployment, use a trusted certificate and drop it.

## Run

```bash
cd src/InventoryApp
dotnet run
```

Or just press F5 in Visual Studio. The product list is the home route.

**You do not need to create the database.** On first start the app connects to LocalDB,
creates the `InventoryApp` database if it is missing, applies the migration and seeds a
realistic catalogue — so a fresh clone goes straight to a populated screen. To start over,
drop the database and run again; it will be rebuilt.

If LocalDB is not installed, startup fails with a single explanatory log line rather than a
raw connection stack trace. Check with `sqllocaldb info`, or point
`ConnectionStrings:DefaultConnection` at any other SQL Server instance.

## Test

```bash
dotnet test
```

The tests create their own databases, so they never touch your development data. See
[Testing approach](#testing-approach) for how to point them at a specific server.

## Migrations

The initial migration is committed, so the app runs on a fresh clone without any
migration step. To add more:

```powershell
# Visual Studio Package Manager Console (Default project: InventoryApp)
Add-Migration <Name> -OutputDir Data/Migrations
Update-Database
```

```bash
# .NET CLI
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> --project src/InventoryApp --output-dir Data/Migrations
dotnet ef database update --project src/InventoryApp
```

The PMC cmdlets come from the `Microsoft.EntityFrameworkCore.Tools` package reference; the
`dotnet ef` CLI uses `Microsoft.EntityFrameworkCore.Design`. Both are referenced. If
`Update-Database` reports *"is not recognized as the name of a cmdlet"*, the Tools package
has not been restored yet — build the solution, then close and reopen the Package Manager
Console, which loads its cmdlets once per session.

`Update-Database` is optional in practice: `DbInitializer.InitializeAsync` calls
`MigrateAsync` on startup, so pending migrations are applied automatically.

## Database setup & seed data

`AppDbContext` maps two tables.

**Products** — `Sku` (`nvarchar(50)`, **unique index**, stored trimmed and upper-cased),
`Name` (`nvarchar(150)`), `Description` (`nvarchar(500)`, optional), `ReorderLevel`
(default 0), `IsActive` (default 1), `ConcurrencyToken` (`uniqueidentifier`,
`[ConcurrencyCheck]`), `CreatedUtc`.

**StockMovements** — `ProductId` (FK, `ON DELETE NO ACTION`), `Type` (`In = 1`, `Out = 2`,
stored as int), `Quantity` (**check constraint `[Quantity] > 0`**), `Note`
(`nvarchar(300)`, optional), `CreatedUtc`. Indexed on `(ProductId, CreatedUtc)` to serve
both the ledger view and the list's stock aggregation.

`DbInitializer` seeds six products whose ledgers produce every state the UI can show —
two in stock, two low stock, one at zero, and one deactivated that keeps its history. It
is idempotent: seeding is skipped once any product exists, so restarts never duplicate
rows. Stock is seeded **as movements**, never as a stock figure, because the ledger is the
only source of truth.

## Assumptions

The brief leaves a few things open. These are the calls I made and why.

- **No authentication.** None was specified, so movements record no user. The ledger is
  append-only already, so it only needs a `CreatedBy` column to become a real audit trail.
- **Movements are immutable.** There is no edit or delete on a movement; a mistake is
  corrected by recording an opposing movement. Editing history would destroy the thing
  that makes derived stock trustworthy.
- **SKUs are case-insensitive.** They are trimmed and upper-cased on every write, so
  `brk-pad-001` and `BRK-PAD-001` are the same product.
- **All times are UTC**, stored and displayed. No time-zone handling was specified, and
  showing UTC explicitly beats silently showing server-local time.
- **A reorder level of 0 means "never flag as low".** Such a product is only ever
  *In stock* or *Out of stock*, which is what a zero threshold should mean.
- **Deactivated products are excluded from the low-stock and out-of-stock counts.** They
  cannot be issued, so counting them would pad the numbers a user acts on.
- **Page size is fixed at 10** (`ProductQuery.DefaultPageSize`). Making it user-selectable
  was not worth the surface area at this scope.
- **Seed data is illustrative** and only applied when the catalogue is empty, so restarts
  never duplicate it.
- **No cost, supplier or location tracking.** Out of scope for the brief.

## Architecture

```
Controllers  ->  Services (IInventoryService)  ->  AppDbContext  ->  SQL Server
   thin             all business logic              EF Core
```

- **Controllers** bind and validate input, call the service, and return a view or a
  redirect. No stock arithmetic, no EF Core calls, no action over ~35 lines.
- **Services** own every rule: stock maths, SKU normalisation, validation, transactions
  and concurrency. `InventoryService` is the only class that writes to the database.
- **Views** render. The one piece of near-logic they touch, the status badge, reads a
  `StockStatus` the service already decided.
- **The product list is rendered once, in one place.** `Index` returns the full page and
  `List` returns the same `_ProductsPanel` partial as a fragment; the browser swaps that
  fragment in over AJAX. There is no second rendering path in JavaScript, so what the user
  sees after an AJAX update is byte-for-byte what a full reload would have produced.
- **Read models** (`Services/InventoryReadModels.cs`) are projected straight from SQL, so
  no entity graph or tracked state ever reaches a view.

### Why a single project with a service layer

The assessment is one bounded module. A generic repository over `DbSet<T>` would only wrap
an abstraction EF Core already provides, and splitting four assemblies apart would add
project references without adding a seam anyone needs. One project with a real service
boundary gives the thing that actually matters — business logic that is testable without a
web server, which is exactly how the xUnit suite drives it. If this module grew a second
consumer (a background job, a public API), `Services/` is the natural extraction point.

### Why soft deactivation instead of deletion

Deleting a product would either orphan or destroy its movement history, and that history
is the audit trail that justifies every stock figure ever reported. `IsActive = false`
hides the product from the default list and blocks new movements while keeping the ledger
intact. `ON DELETE NO ACTION` on the foreign key makes this a database guarantee rather
than a convention, and reactivation is a single toggle.

### How concurrent negative stock is prevented

This is the core of the module, and a transaction alone does not solve it.

Two requests can read "stock 8" at the same moment — under READ COMMITTED the shared locks
those reads take are released at the end of each statement, so neither request blocks the
other. Both pass a `quantity > currentStock` check and both try to issue 8. Wrapping each
one in its own transaction makes each *atomic*, but still lets both commit, leaving stock
at −8.

The fix is that every movement write also touches the **parent product row**:

1. `BeginTransactionAsync` makes the stock check and the ledger insert one atomic unit, so
   a failure between them cannot leave the ledger half-written.
2. The same write re-stamps `Product.ConcurrencyToken` with a new `Guid`. Because the
   property is marked `[ConcurrencyCheck]`, EF Core emits:

   ```sql
   UPDATE Products SET ConcurrencyToken = @new
   WHERE Id = @id AND ConcurrencyToken = @originalValueThisRequestRead
   ```

   Whichever request reaches that statement first takes an exclusive lock on the product
   row and holds it until it commits, so the second request queues behind it rather than
   racing it. When the second one is released, the token has changed, its `UPDATE` matches
   **zero rows**, EF Core raises `DbUpdateConcurrencyException`, its transaction rolls
   back, and the ledger never sees the insert.
3. `RecordMovementAsync` wraps the attempt in a bounded retry loop (4 attempts). On
   conflict it clears the change tracker — so the retry reads fresh data instead of the
   stale cached entity — and tries again against the real stock. The retry either succeeds
   or fails correctly with *"Cannot issue 8 units. Only 0 units are currently in stock."*

Every movement for a product funnels through that one row, which makes the product row the
serialisation point for its own ledger. Because every writer requests the same lock on the
same row in the same order, contending requests queue rather than deadlock.

**Why a `Guid` token rather than `rowversion`.** SQL Server's `rowversion` with
`[Timestamp]` is the more idiomatic choice when concurrency is incidental — you edit a row,
and EF checks nobody else did. Here the concurrency check is the *point* of the write, and
the write does not otherwise change the product at all. With `rowversion`, EF would see an
unmodified entity, issue no `UPDATE`, and silently perform no concurrency check; making it
work would mean forcing `IsModified` on a property the application is not allowed to set.
Re-stamping a `Guid` the application owns expresses the intent directly: *this movement
claims this product row*. It also keeps the service provider-agnostic.

**Why `EnableRetryOnFailure` is not configured.** Its retrying execution strategy refuses
user-initiated transactions, and this service opens one explicitly for every movement
write. Enabling it would require wrapping that transaction in
`Database.CreateExecutionStrategy().ExecuteAsync(...)` first — worth doing against Azure
SQL, unnecessary against a local instance, and silently breaking if added without the
wrapper.

### Unique SKU enforcement

Two layers, on purpose. `InventoryService` checks for an existing SKU first, which
produces a friendly field-level message in the normal case. A unique database index backs
it up for the race the pre-flight check cannot win, and a `DbUpdateException` wrapping SQL
Server error 2601 or 2627 is translated into the same `DuplicateSkuException`, so the user
sees one consistent message either way. SKUs are trimmed and upper-cased before every
write, which makes a plain unique index enforce case-insensitive uniqueness regardless of
the database collation.

### Error handling

`UseExceptionHandler("/Home/Error")` is registered in **every** environment, including
Development — the developer exception page is deliberately not used, because the
requirement is that a stack trace never reaches the browser and the only way to know the
real error page works is to use it. `UseStatusCodePagesWithReExecute` turns a bare 404 into
the custom `NotFound` view while preserving the status code. `ErrorViewModel` carries only
a trace identifier, so there is nothing sensitive for a page to leak.

Expected, user-correctable failures all derive from `DomainException`, which lets a
controller catch business problems and show them as validation messages while genuine
bugs keep bubbling up to the global handler.

### Validation

Server-side validation is the authority. Every rule is enforced in `InventoryService`,
so a request that never touches a browser — a direct POST, a script, a future API client
— is rejected identically. `ModelState` checks in the controllers catch the same rules a
step earlier, from the DataAnnotations on the ViewModels.

Client-side validation (`jquery-validation` + unobtrusive, the pair that ships with the
ASP.NET Core templates) is layered on top purely so the user gets immediate feedback.
Nothing depends on it: disable JavaScript and every rule still holds.

### Query efficiency

Listing products runs exactly **two statements**: a `COUNT` and a page fetch
(`ORDER BY ... OFFSET ... FETCH NEXT`). Both are built from one intermediate projection,
so the search, the bucket filter, the count and the page all reuse the same aggregate.
Stock itself is computed **by the database** as one correlated subquery per row:

```csharp
p.Movements.Sum(m => m.Type == MovementType.In ? m.Quantity : -m.Quantity)
```

No movement entities are loaded and there is no per-product follow-up query, so neither a
growing ledger nor a growing catalogue adds round trips. Every read query uses `AsNoTracking()` with a
`.Select(...)` projection, and every EF Core call is `async` and takes a
`CancellationToken`.

## Stretch goals implemented

- **Summary cards that filter** — five buckets (all, active, inactive, low stock, out of
  stock). Each card shows its count and selects that bucket when clicked. The counts and
  the filter share one `ProductFilter` enum, so a card can never disagree with the rows it
  produces — a test asserts exactly that.
- **CSV export** — `/Products/ExportProducts` streams the product list exactly as
  filtered on screen, and `/Products/ExportMovements/{id}` streams one product's ledger
  oldest-first with a running balance after each entry. Both use RFC 4180 quoting, a
  single header row so the grid lines up in Excel, a UTF-8 BOM for non-ASCII text, and
  neutralise leading `=`, `+`, `-` and `@@` in user-entered fields to prevent CSV
  injection.
- **JSON stock endpoint** — `GET /api/products/{id}/stock` returns
  `{ productId, sku, name, currentStock, reorderLevel, isLowStock }`, or a JSON 404.
- **Live search** — case-insensitive match on SKU or name, debounced so one request
  follows a pause in typing rather than one per keystroke.
- **Server-side pagination over AJAX** — only the requested page is fetched from the
  database. Paging, filtering, searching and activating a product all update one region
  of the page without a reload.

### Client-side behaviour

`wwwroot/js/products.js` is about 100 lines of jQuery and holds three variables of state:
the search term, the selected bucket and the page number. Every interaction changes one of
them, asks the server for the panel, and swaps in the HTML. Handlers are bound by
delegation because the panel's contents are replaced on each refresh.

Activating or deactivating a product posts to `SetActive`, which answers with JSON, and
then re-requests the panel — the summary counts move with the product, so refreshing the
whole region is both simpler and more correct than patching the single row.

## Testing approach

`dotnet test` runs against a **real SQL Server database** rather than the EF Core in-memory
provider, so transactions, the unique index, the `[Quantity] > 0` check constraint and
row-level locking are all genuinely exercised — the in-memory provider implements none of
them, so a suite built on it would pass while the real thing broke.

`SqlServerTestDatabase` creates a uniquely named database per test, applies the **real
migration** — a broken migration fails in CI rather than in production — and drops the
database afterwards. By default it uses the LocalDB instance that ships with Visual Studio.
Point it elsewhere with an environment variable:

```powershell
$env:INVENTORYAPP_TEST_SERVER = "DESKTOP-5LAQO8V"
dotnet test
```

Leftover databases, if a drop is ever blocked, are all named `InventoryApp_Test_*`.

Covered: server-side paging (page boundaries, totals, no overlap between pages); every
bucket filter agreeing with the count on its card; stock arithmetic across mixed movements; an `Out` movement exceeding stock
(asserting the exact message and that nothing was written); non-positive quantities;
case-insensitive duplicate SKUs on create and on edit; soft deactivation preserving
history, blocking movements and being reversible; and eight parallel `Out` requests
against stock 10, asserting stock never goes negative, that the ledger agrees exactly with
how many requests succeeded, and that every loser failed with a `DomainException` rather
than a raw database error.

## What I would do next

- **Pagination and server-side filtering** on the product list. It currently returns every
  matching row, which is right for a module of this size and wrong past a few thousand
  SKUs.
- **A stock snapshot** — a cached `CurrentStock` column maintained inside the same
  transaction as the movement, or an indexed view. The ledger stays authoritative; the
  snapshot only removes the per-row aggregation from list queries. `GetKpisAsync` is the
  first place that would benefit, and it is marked in the code.
- **Authentication and authorisation**, with the user recorded on each movement. The
  ledger is already append-only, so it only needs a `CreatedBy` column to become a real
  audit trail.
- **Integration tests** over the HTTP surface with `WebApplicationFactory`, to cover
  routing, model binding, antiforgery and the error pages — the service layer is well
  covered, the pipeline is not.
- **Movement reversal as a first-class action.** Corrections are currently made by
  recording an opposing movement by hand; a "reverse this movement" button with a link
  back to the original entry would make the intent explicit in the history.
