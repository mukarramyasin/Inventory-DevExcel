using System.Globalization;
using System.Text;
using InventoryApp.Domain.Entities;

namespace InventoryApp.Services;

/// <summary>
/// RFC 4180 CSV that opens cleanly in Excel: one header row, data straight after. No
/// title or metadata lines above the header, which would throw the grid out of alignment.
/// </summary>
public static class InventoryCsvExporter
{
    /// <summary>The product list exactly as filtered on screen.</summary>
    public static byte[] ProductsToCsv(IReadOnlyList<ProductStockRow> products)
    {
        var csv = new StringBuilder();
        csv.AppendLine("SKU,Name,Current Stock,Reorder Level,Status,Active");

        foreach (var product in products)
        {
            csv.AppendLine(string.Join(',',
                Text(product.Sku), Text(product.Name), Number(product.CurrentStock),
                Number(product.ReorderLevel), Text(StockStatusEvaluator.Label(product.Status)),
                product.IsActive ? "Yes" : "No"));
        }

        return WithBom(csv);
    }

    /// <summary>One product's ledger, oldest first so the running balance reads correctly.</summary>
    public static byte[] MovementsToCsv(ProductDetail product)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Date (UTC),SKU,Product,Type,Quantity,Balance After,Note");

        var balance = 0;

        foreach (var movement in product.Movements.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id))
        {
            balance += movement.Type == MovementType.In ? movement.Quantity : -movement.Quantity;

            csv.AppendLine(string.Join(',',
                movement.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Text(product.Sku), Text(product.Name), movement.Type.ToString().ToUpperInvariant(),
                Number(movement.Quantity), Number(balance), Text(movement.Note)));
        }

        return WithBom(csv);
    }

    public static string ProductsFileName() => $"products-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";

    public static string MovementsFileName(ProductDetail product) => $"movements-{Slug(product.Sku)}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";

    /// <summary>UTF-8 BOM, so Excel reads non-ASCII text instead of the system code page.</summary>
    private static byte[] WithBom(StringBuilder csv)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv.ToString());

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);

        return bytes;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Quotes a user-entered field when it needs it, and neutralises a leading =, +, - or
    /// @ with an apostrophe so a spreadsheet cannot treat the cell as a formula. Numbers
    /// go through <see cref="Number"/>, so negatives are unaffected.
    /// </summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        var needsQuoting = text.AsSpan().IndexOfAny(',', '"', '\n') >= 0 || text.Contains('\r');

        return needsQuoting ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static string Slug(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
}
