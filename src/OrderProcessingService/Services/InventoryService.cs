using System.Diagnostics;
using System.Net.Http.Json;
using OrderProcessingService.Models;
using OrderProcessingService.Telemetry;
using Shared.Library.Telemetry;
using Shared.Library.Models;
using Shared.Library.Policies;
using Shared.Library.Telemetry.Baggage;

namespace OrderProcessingService.Services;

public class InventoryService : IInventoryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryService> _logger;
    private readonly BaggageManager _baggageManager;

    public InventoryService(
        HttpClient httpClient, 
        ILogger<InventoryService> logger,
        BaggageManager baggageManager)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baggageManager = baggageManager;
    }

    public async Task<InventoryReservationResult> CheckAndReserveInventoryAsync(
        int orderId, 
        int productId, 
        int quantity,
        bool isPriority = false)
    {
        try
        {
            // Set business context
            _baggageManager.SetOrderContext(orderId.ToString(), 
                priority: isPriority ? "High" : "Standard");
            
            // Create a retry policy that adjusts based on priority
            var retryPolicy = PriorityBasedPolicy.CreatePriorityAwareRetryPolicy<InventoryReservationResult>(
                _baggageManager,
                standardRetryCount: 2,    // Standard customers get 2 retries
                premiumRetryCount: 5);    // Premium customers get 5 retries
                
            // Create context for the policy
            var context = new Polly.Context
            {
                ["logger"] = _logger,
                ["operationKey"] = "ReserveInventory"
            };
                
            // Execute with the retry policy
            return await retryPolicy.ExecuteAsync(async ctx =>
            {
                // Add operation-specific span tags
                using var activity = Activity.Current;
                activity?.SetTag("order.id", orderId);
                activity?.SetTag("product.id", productId);
                activity?.SetTag("inventory.quantity", quantity);
                activity?.SetTag("inventory.priority", isPriority);
                
                // Get the correlation ID from baggage
                var correlationId = _baggageManager.GetCorrelationId();
                
                // Create the reservation request
                var request = new InventoryReservationRequest
                {
                    OrderId = orderId,
                    ProductId = productId,
                    Quantity = quantity,
                    CorrelationId = correlationId,
                    IsPriority = isPriority
                };
                
                // If this is a high priority request, add a custom header
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/inventory/reserve");
                if (isPriority)
                {
                    requestMessage.Headers.Add("X-Priority", "High");
                }
                
                // Send the request
                var response = await _httpClient.PostAsJsonAsync<InventoryReservationRequest, InventoryReservationResult>(
                    "api/inventory/reserve", request);
                    
                if (response == null)
                {
                    throw new InvalidOperationException("Failed to get a response from the inventory service");
                }
                
                return response;
            }, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reserving inventory for order {OrderId}, product {ProductId}", 
                orderId, productId);
                
            return new InventoryReservationResult
            {
                Success = false,
                Message = $"Inventory reservation failed: {ex.Message}"
            };
        }
    }
}

public interface IInventoryService
{
    Task<InventoryReservationResult> CheckAndReserveInventoryAsync(
        int orderId, 
        int productId, 
        int quantity,
        bool isPriority = false);
}

public class InventoryReservationRequest
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string CorrelationId { get; set; }
    public bool IsPriority { get; set; }
}

public class InventoryReservationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? ReservationId { get; set; }
}
