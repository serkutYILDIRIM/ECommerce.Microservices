using System.Net.Http.Json;
using Shared.Library.Models;

namespace Shared.Library.Clients.Implementation;

public class OrderProcessingClient : IOrderProcessingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrderProcessingClient> _logger;

    public OrderProcessingClient(HttpClient httpClient, ILogger<OrderProcessingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<OrderDto>> GetOrdersAsync()
    {
        try
        {
            var orders = await _httpClient.GetFromJsonAsync<List<OrderDto>>("/orders");
            return orders ?? new List<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders");
            return new List<OrderDto>();
        }
    }

    public async Task<OrderDto?> GetOrderAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<OrderDto>($"/orders/{id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order with ID {OrderId}", id);
            return null;
        }
    }

    public async Task<OrderDto> CreateOrderAsync(OrderDto order)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/orders", order);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }

    public async Task<OrderDto?> UpdateOrderStatusAsync(int id, UpdateOrderStatusDto statusUpdate)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/orders/{id}/status", statusUpdate);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for order with ID {OrderId}", id);
            return null;
        }
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/orders/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting order with ID {OrderId}", id);
            return false;
        }
    }
}
