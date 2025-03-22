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
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int OrderId { get; set; }
    public int Quantity { get; set; }
    public DateTime ReservationDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string Status { get; set; }
    public bool Priority { get; set; }
    public string CorrelationId { get; set; }
    public string CustomerTier { get; set; }
    public string Notes { get; set; }
}

