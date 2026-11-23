# AI Usage Notes

> **Before submitting:** replace every `[...]` placeholder and delete this blockquote.
> Section 4 is the one reviewers read most closely — make sure it describes an issue
> *you* actually verified, in your own words.

## 1. Tools used

| Tool | Version / model | Used for |
|------|-----------------|----------|
| [tool name] | [version] | [scaffolding / tests / docs] |
| [tool name] | [version] | [...] |

## 2. Where AI was used

- **Project scaffolding** — folder layout, `.csproj` files, the solution file, and the
  initial `AppDbContext` mapping.
- **Boilerplate** — domain exception types, ViewModel DataAnnotations, and the Razor
  views' Bootstrap markup.
- **Test scaffolding** — the shape of the xUnit suite and the per-test SQL Server
  database harness.
- **Documentation** — first draft of `README.md`.

## 3. Where AI was *not* used / where I drove the design

- [e.g. "I chose the ledger-only stock model over a cached stock column, and rejected the
  first suggestion that added a `Stock` property to `Product`."]
- [e.g. "I specified the concurrency strategy — transaction plus `[ConcurrencyCheck]`
  token on the parent row — after reasoning about why a transaction alone is not enough."]
- [e.g. "I reviewed every generated file line by line and rewrote [...]."]

## 4. An AI mistake I caught and corrected

> This section must describe one concrete, specific defect — not "I checked the output
> carefully". Reviewers are looking for evidence that you can spot a plausible-looking
> error. One real example you can verify in this codebase is pre-filled below; keep it
> only if you confirmed it yourself, otherwise replace it with your own.

**What the AI produced**

The details view posted the movement form with fields named `NewMovement.Quantity`,
`NewMovement.Type` and so on, because the view's model is `ProductDetailsViewModel` and
the fields were written as `asp-for="NewMovement.Quantity"`. The controller action,
however, was generated as:

```csharp
public async Task<IActionResult> RecordMovement(StockMovementInputViewModel input, ...)
```

**Why it looked correct**

It compiles, the view renders, and the form submits without any error. The types line up
and nothing in the editor flags a problem.

**What actually happens**

ASP.NET Core model binding derives its prefix from the *parameter name*. With the
parameter called `input`, the binder looks for `input.Quantity` and finds nothing, then
falls back to the empty prefix and looks for `Quantity` — which is also absent, because
the form posts `NewMovement.Quantity`. So every field binds to its default: `Quantity`
arrives as `0` and `ProductId` as `0`. The result is a validation error on a quantity the
user never entered, or a redirect to product `0`, and no movement is ever recorded.

**The fix**

Rename the parameter so it matches the binding prefix the view actually emits (matching is
case-insensitive):

```csharp
public async Task<IActionResult> RecordMovement(StockMovementInputViewModel newMovement, ...)
```

**Why it matters**

This is the class of AI error that is most dangerous: not a compile failure, but a silent
mismatch between two files that each look right in isolation. It would only have surfaced
by actually submitting the form, which is exactly why the generated code was run and not
just read.

## 5. Verification I performed myself

- [ ] `dotnet build` with zero warnings
- [ ] `dotnet test` — all tests pass
- [ ] Ran the app and exercised each screen: list, search, inactive filter, create,
      duplicate SKU, edit, deactivate/reactivate, record `In`, record `Out`, over-issue
      rejection, CSV export
- [ ] Hit `/api/products/1/stock` and a non-existent id for the JSON 404
- [ ] Forced an error and a 404 to confirm no stack trace is shown
- [ ] [anything else you checked]

## 6. Known limitations

- The product list has no pagination; every matching row is returned.
- `GetKpisAsync` projects all active products and counts them in memory, which is fine at
  this scale and flagged in a code comment as the first thing to move into SQL.
- No authentication, so movements record no user.
- `EnableRetryOnFailure` is not configured, because its execution strategy is incompatible
  with the explicit transaction used for every movement write. This matters only against a
  remote or cloud database.
