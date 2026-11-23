namespace InventoryApp.Domain.Exceptions;

public sealed class ProductNotFoundException(int productId)
    : DomainException($"Product {productId} was not found.")
{
    public int ProductId { get; } = productId;
}
