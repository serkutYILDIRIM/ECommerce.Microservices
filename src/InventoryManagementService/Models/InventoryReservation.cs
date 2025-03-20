namespace InventoryManagementService.Models;

/// <summary>
/// Represents a reservation of inventory for an order
/// </summary>
public class InventoryReservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int OrderId { get; set; }
    public int Quantity { get; set; }
    public DateTime ReservationDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string Status { get; set; } // "Active", "Used", "Expired", "Cancelled"
    public bool Priority { get; set; }
    public string CorrelationId { get; set; }
    public string CustomerTier { get; set; }
    public string Notes { get; set; }
    
    // Navigation property
    public InventoryItem InventoryItem { get; set; }
}
