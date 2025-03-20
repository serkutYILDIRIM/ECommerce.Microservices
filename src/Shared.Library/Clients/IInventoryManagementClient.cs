using Shared.Library.Models;

namespace Shared.Library.Clients;

public interface IInventoryManagementClient
{
    Task<List<InventoryDto>> GetInventoryItemsAsync();
    Task<InventoryDto?> GetInventoryItemAsync(int id);
    Task<InventoryDto?> GetInventoryByProductIdAsync(int productId);
    Task<InventoryCheckResultDto?> CheckInventoryAsync(int productId, int quantity);
    Task<InventoryDto> CreateInventoryItemAsync(InventoryDto inventoryItem);
    Task<InventoryDto?> UpdateInventoryItemAsync(int id, InventoryDto inventoryItem);
    Task<InventoryReservationResultDto?> ReserveInventoryAsync(InventoryReservationDto reservation);
    Task<bool> DeleteInventoryItemAsync(int id);
}
