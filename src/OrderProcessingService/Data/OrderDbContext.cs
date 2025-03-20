using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Models;

namespace OrderProcessingService.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) 
        : base(options)
    {
    }

    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure relationships
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId);

        // Seed some initial data
        var order1 = new Order
        {
            Id = 1,
            CustomerName = "John Doe",
            CustomerEmail = "john.doe@example.com",
            TotalAmount = 1299.99m,
            Status = OrderStatus.Processing,
            OrderDate = DateTime.UtcNow.AddDays(-2)
        };

        var order2 = new Order
        {
            Id = 2,
            CustomerName = "Jane Smith",
            CustomerEmail = "jane.smith@example.com",
            TotalAmount = 99.95m,
            Status = OrderStatus.Pending,
            OrderDate = DateTime.UtcNow.AddDays(-1)
        };

        modelBuilder.Entity<Order>().HasData(order1, order2);

        modelBuilder.Entity<OrderItem>().HasData(
            new OrderItem
            {
                Id = 1,
                OrderId = 1,
                ProductId = 1,
                ProductName = "Laptop",
                UnitPrice = 1299.99m,
                Quantity = 1
            },
            new OrderItem
            {
                Id = 2,
                OrderId = 2,
                ProductId = 3,
                ProductName = "Coffee Maker",
                UnitPrice = 99.95m,
                Quantity = 1
            }
        );
    }
}
