namespace InventoryApp.Domain.Exceptions;

/// <summary>Carries both figures so the user is told exactly what is available.</summary>
public sealed class InsufficientStockException(int requested, int available)
    : DomainException($"Cannot issue {requested} units. Only {available} units are currently in stock.")
{
    public int Requested { get; } = requested;

    public int Available { get; } = available;
}
