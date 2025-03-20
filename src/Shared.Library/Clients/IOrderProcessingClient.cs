using Shared.Library.Models;

namespace Shared.Library.Clients;

public interface IOrderProcessingClient
{
    Task<List<OrderDto>> GetOrdersAsync();
    Task<OrderDto?> GetOrderAsync(int id);
    Task<OrderDto> CreateOrderAsync(OrderDto order);
    Task<OrderDto?> UpdateOrderStatusAsync(int id, UpdateOrderStatusDto statusUpdate);
    Task<bool> DeleteOrderAsync(int id);
}
