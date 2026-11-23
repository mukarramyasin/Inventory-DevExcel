namespace InventoryApp.Domain.Exceptions;

/// <summary>Deactivated products keep their history but are closed to new movements.</summary>
public sealed class InactiveProductException(int productId, string sku)
    : DomainException($"Product '{sku}' is deactivated and cannot accept new stock movements. Reactivate it first.")
{
    public int ProductId { get; } = productId;
}
