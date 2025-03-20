namespace InventoryManagementService.Models;

public class InventoryItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantityAvailable { get; set; }
    public int QuantityReserved { get; set; }
    public int ReorderThreshold { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateTime LastRestocked { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class InventoryReservation
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
