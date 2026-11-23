namespace InventoryApp.Domain.Exceptions;

/// <summary>Raised by the service's pre-flight check and by the unique index catching a race.</summary>
public sealed class DuplicateSkuException(string sku)
    : DomainException($"SKU '{sku}' is already in use by another product. Please enter a different SKU.")
{
    public string Sku { get; } = sku;
}
