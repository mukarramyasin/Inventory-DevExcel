using System.ComponentModel.DataAnnotations;
using InventoryApp.Domain.Entities;

namespace InventoryApp.Models;

/// <summary>Inline "record a movement" form on the details screen.</summary>
public class StockMovementInputViewModel
{
    public int ProductId { get; set; }

    [Display(Name = "Movement type")]
    public MovementType Type { get; set; } = MovementType.In;

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; set; } = 1;

    [StringLength(300, ErrorMessage = "Note cannot exceed 300 characters.")]
    public string? Note { get; set; }
}
