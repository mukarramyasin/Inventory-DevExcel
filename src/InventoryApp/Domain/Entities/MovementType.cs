namespace InventoryApp.Domain.Entities;

/// <summary>Direction of a ledger entry. Values are explicit because they are persisted.</summary>
public enum MovementType
{
    In = 1,
    Out = 2
}
