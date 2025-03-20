namespace Shared.Library.Models;

public class InventoryDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantityAvailable { get; set; }
    public int QuantityReserved { get; set; }
    public int ReorderThreshold { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateTime LastRestocked { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class InventoryCheckResultDto
{
    public bool Available { get; set; }
    public int AvailableQuantity { get; set; }
}

public class InventoryReservationDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class InventoryReservationResultDto
{
    public bool Reserved { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int RemainingAvailable { get; set; }
}
