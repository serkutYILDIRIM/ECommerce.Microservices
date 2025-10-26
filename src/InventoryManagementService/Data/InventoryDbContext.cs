using InventoryManagementService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagementService.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<InventoryItem> InventoryItems { get; set; }
    public DbSet<InventoryReservation> InventoryReservations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the InventoryItem entity
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.ProductId);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.QuantityAvailable).IsRequired();
            entity.Property(e => e.QuantityReserved).IsRequired();
            entity.Property(e => e.LastUpdated).IsRequired();
        });

        // Configure the InventoryReservation entity
        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductId).IsRequired();
            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.Quantity).IsRequired();
            entity.Property(e => e.ReservationDate).IsRequired();
            entity.Property(e => e.ExpiryDate).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Priority).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(50);
            entity.Property(e => e.CustomerTier).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(500);

            // Define relationship with InventoryItem
            entity.HasOne<InventoryItem>()
                  .WithMany()
                  .HasForeignKey(e => e.ProductId);
        });

        // Seed some initial data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Add sample inventory items
        modelBuilder.Entity<InventoryItem>().HasData(
            new InventoryItem
            {
                ProductId = 1,
                ProductName = "Widget A",
                QuantityAvailable = 100,
                QuantityReserved = 10,
                LastUpdated = DateTime.UtcNow
            },
            new InventoryItem
            {
                ProductId = 2,
                ProductName = "Widget B",
                QuantityAvailable = 50,
                QuantityReserved = 5,
                LastUpdated = DateTime.UtcNow
            },
            new InventoryItem
            {
                ProductId = 3,
                ProductName = "Gadget C",
                QuantityAvailable = 75,
                QuantityReserved = 15,
                LastUpdated = DateTime.UtcNow
            }
        );
    }
}
