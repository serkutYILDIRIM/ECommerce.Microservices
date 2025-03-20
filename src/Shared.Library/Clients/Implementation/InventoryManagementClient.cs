using System.Net.Http.Json;
using Shared.Library.Models;

namespace Shared.Library.Clients.Implementation;

public class InventoryManagementClient : IInventoryManagementClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryManagementClient> _logger;

    public InventoryManagementClient(HttpClient httpClient, ILogger<InventoryManagementClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<InventoryDto>> GetInventoryItemsAsync()
    {
        try
        {
            var items = await _httpClient.GetFromJsonAsync<List<InventoryDto>>("/inventory");
            return items ?? new List<InventoryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory items");
            return new List<InventoryDto>();
        }
    }

    public async Task<InventoryDto?> GetInventoryItemAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<InventoryDto>($"/inventory/{id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory item with ID {InventoryId}", id);
            return null;
        }
    }

    public async Task<InventoryDto?> GetInventoryByProductIdAsync(int productId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<InventoryDto>($"/inventory/product/{productId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory for product with ID {ProductId}", productId);
            return null;
        }
    }

    public async Task<InventoryCheckResultDto?> CheckInventoryAsync(int productId, int quantity)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<InventoryCheckResultDto>($"/inventory/check/{productId}?quantity={quantity}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking inventory for product {ProductId}", productId);
            return null;
        }
    }

    public async Task<InventoryDto> CreateInventoryItemAsync(InventoryDto inventoryItem)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/inventory", inventoryItem);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<InventoryDto>())!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory item");
            throw;
        }
    }

    public async Task<InventoryDto?> UpdateInventoryItemAsync(int id, InventoryDto inventoryItem)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/inventory/{id}", inventoryItem);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<InventoryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating inventory item with ID {InventoryId}", id);
            return null;
        }
    }

    public async Task<InventoryReservationResultDto?> ReserveInventoryAsync(InventoryReservationDto reservation)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/inventory/reserve", reservation);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<InventoryReservationResultDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving inventory for product {ProductId}", reservation.ProductId);
            return null;
        }
    }

    public async Task<bool> DeleteInventoryItemAsync(int id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/inventory/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inventory item with ID {InventoryId}", id);
            return false;
        }
    }
}
