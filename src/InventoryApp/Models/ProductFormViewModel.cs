using System.ComponentModel.DataAnnotations;
using InventoryApp.Services;

namespace InventoryApp.Models;

/// <summary>Shared form model for Create and Edit. Attributes mirror the database constraints.</summary>
public class ProductFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "SKU is required.")]
    [StringLength(50, ErrorMessage = "SKU cannot exceed 50 characters.")]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150, ErrorMessage = "Name cannot exceed 150 characters.")]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    [DataType(DataType.MultilineText)]
    public string? Description { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Reorder level cannot be negative.")]
    [Display(Name = "Reorder level")]
    public int ReorderLevel { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public bool IsEdit => Id > 0;

    public ProductWriteModel ToWriteModel() => new(Sku, Name, Description, ReorderLevel, IsActive);

    public static ProductFormViewModel FromWriteModel(int id, ProductWriteModel product) =>
        new()
        {
            Id = id,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            ReorderLevel = product.ReorderLevel,
            IsActive = product.IsActive
        };
}
