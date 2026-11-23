namespace InventoryApp.Domain.Exceptions;

/// <summary>Raised when the retry budget is spent, instead of leaking DbUpdateConcurrencyException.</summary>
public sealed class ConcurrencyConflictException()
    : DomainException("This product's stock was changed by another user while your request was being saved. Please refresh and try again.")
{
}
