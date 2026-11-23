namespace InventoryApp.Domain.Exceptions;

public sealed class InvalidQuantityException(int quantity)
    : DomainException($"Quantity must be greater than zero. Received {quantity}.")
{
    public int Quantity { get; } = quantity;
}
