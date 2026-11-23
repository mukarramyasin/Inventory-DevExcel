using InventoryApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Data;

/// <summary>Mapping lives here so the entities stay free of persistence concerns.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Sku).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(150);
            entity.Property(p => p.Description).HasMaxLength(500);
            entity.Property(p => p.ReorderLevel).HasDefaultValue(0);
            entity.Property(p => p.IsActive).HasDefaultValue(true);

            // SKUs are upper-cased before every write, so a plain unique index gives
            // case-insensitive uniqueness. Last line of defence against a create race.
            entity.HasIndex(p => p.Sku).IsUnique();
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasConversion<int>().IsRequired();
            entity.Property(m => m.Quantity).IsRequired();
            entity.Property(m => m.Note).HasMaxLength(300);

            // Restrict, because products are deactivated and never deleted.
            entity.HasOne(m => m.Product).WithMany(p => p.Movements)
                  .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);

            // Serves both the ledger view and the list's stock aggregation.
            entity.HasIndex(m => new { m.ProductId, m.CreatedUtc });

            // Makes "quantity is positive" true of the data, not just of the code paths.
            entity.ToTable(t => t.HasCheckConstraint("CK_StockMovement_Quantity_Positive", "[Quantity] > 0"));
        });
    }
}
