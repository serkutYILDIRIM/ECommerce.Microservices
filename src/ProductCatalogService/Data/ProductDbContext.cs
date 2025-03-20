using Microsoft.EntityFrameworkCore;
using ProductCatalogService.Models;

namespace ProductCatalogService.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) 
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Seed some initial data
        modelBuilder.Entity<Product>().HasData(
            new Product
            {
                Id = 1,
                Name = "Laptop",
                Description = "High-performance laptop for developers",
                Price = 1299.99m,
                StockQuantity = 50,
                Category = "Electronics"
            },
            new Product
            {
                Id = 2,
                Name = "Smartphone",
                Description = "Latest smartphone with advanced camera",
                Price = 899.99m,
                StockQuantity = 100,
                Category = "Electronics"
            },
            new Product
            {
                Id = 3,
                Name = "Coffee Maker",
                Description = "Automatic coffee maker with timer",
                Price = 149.99m,
                StockQuantity = 30,
                Category = "Home Appliances"
            }
        );
    }
}
