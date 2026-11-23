namespace InventoryApp.Domain.Exceptions;

/// <summary>
/// Base type for expected, user-correctable failures. Controllers catch this and show a
/// validation message; anything else is a bug and reaches the global error handler.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}
