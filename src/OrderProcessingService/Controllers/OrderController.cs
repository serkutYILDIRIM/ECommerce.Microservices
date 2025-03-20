using Microsoft.AspNetCore.Mvc;
using OrderProcessingService.Data;
using OrderProcessingService.Models;
using OrderProcessingService.Services;
using Shared.Library.Services;
using Shared.Library.Telemetry.Baggage;
using System.Diagnostics;

namespace OrderProcessingService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly OrderDbContext _dbContext;
    private readonly IProductCatalogService _productService;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<OrderController> _logger;
    private readonly BaggageManager _baggageManager;
    private readonly BusinessContextEnricher _contextEnricher;

    public OrderController(
        OrderDbContext dbContext,
        IProductCatalogService productService,
        IInventoryService inventoryService,
        ILogger<OrderController> logger,
        BaggageManager baggageManager,
        BusinessContextEnricher contextEnricher)
    {
        _dbContext = dbContext;
        _productService = productService;
        _inventoryService = inventoryService;
        _logger = logger;
        _baggageManager = baggageManager;
        _contextEnricher = contextEnricher;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(int id)
    {
        // Set order context in baggage
        _baggageManager.SetOrderContext(id.ToString());
        
        var order = await _dbContext.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        // Set customer context in baggage
        _baggageManager.SetCustomerContext(order.CustomerId.ToString(), 
            customerTier: GetCustomerTier(order.CustomerId));
            
        // Set transaction context for cross-service tracking
        _baggageManager.SetTransactionContext();

        return order;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(Order order)
    {
        try
        {
            // Set business context in baggage
            _baggageManager.SetCustomerContext(order.CustomerId.ToString(), 
                customerTier: GetCustomerTier(order.CustomerId));
            _baggageManager.SetTransactionContext();
            
            // Capture the business context for the operation
            var context = _contextEnricher.GetBusinessContext();
            
            // Enrich the order with context information
            order = _contextEnricher.EnrichEntity(order);
            order.OrderDate = DateTime.UtcNow;
            order.Status = "Created";
            
            // Set order priority based on customer tier
            if (context.IsPremiumCustomer)
            {
                order.Priority = "High";
                _baggageManager.Set(BaggageManager.Keys.OrderPriority, "High");
            }
            
            // Track the operation with business context
            using var activity = new ActivitySource("OrderProcessing").StartActivity("CreateOrder");
            activity?.SetTag("order.customer_id", order.CustomerId);
            activity?.SetTag("order.total", order.Total);
            activity?.SetTag("business.customer_tier", context.CustomerTier);
            activity?.SetTag("business.priority", order.Priority);
            
            // Process according to customer tier
            await _baggageManager.WhenCustomerTier("premium").ExecuteAsync(async () => 
            {
                // Special handling for premium customers
                _logger.LogInformation("Processing premium customer order with expedited handling");
                order.Notes = order.Notes + " (Premium customer - expedited handling)";
            });
            
            // Save the order
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
            
            // Update baggage with the new order ID
            _baggageManager.SetOrderContext(order.Id.ToString(), order.Total, order.Priority);
            
            // Return success
            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }
        catch (Exception ex)
        {
            // Log error with business context
            _logger.LogError(ex, "Error creating order for customer {CustomerId}. Context: {Context}", 
                order.CustomerId, _contextEnricher.GetBusinessContext());
            
            return StatusCode(500, "An error occurred while processing your order");
        }
    }

    [HttpPost("{id}/process")]
    public async Task<ActionResult<OrderProcessResult>> ProcessOrder(int id)
    {
        // Set business context for this operation
        _baggageManager.SetOrderContext(id.ToString());
        _baggageManager.SetTransactionContext();
        
        var order = await _dbContext.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
            
        // Set customer context in baggage
        _baggageManager.SetCustomerContext(order.CustomerId.ToString(), 
            customerTier: GetCustomerTier(order.CustomerId));
        
        // Get business context from baggage
        var context = _contextEnricher.GetBusinessContext();
        
        // Process with priority handling if needed
        if (context.IsPremiumCustomer || context.IsHighPriority)
        {
            _logger.LogInformation("Processing high priority order {OrderId} for {CustomerTier} customer",
                id, context.CustomerTier);
                
            // Fast path processing for premium customers or high priority orders
            return await ProcessOrderWithPriority(order);
        }
        else
        {
            // Standard processing path
            return await ProcessOrderStandard(order);
        }
    }
    
    private async Task<OrderProcessResult> ProcessOrderWithPriority(Order order)
    {
        // Priority processing logic
        using var activity = new ActivitySource("OrderProcessing").StartActivity("PriorityOrderProcessing");
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("order.customer_id", order.CustomerId);
        activity?.SetTag("order.priority", "high");
        
        // Verify inventory with priority flag
        var inventoryResult = await _inventoryService.CheckAndReserveInventoryAsync(
            order.Id,
            order.ProductId,
            order.Quantity,
            isPriority: true);
            
        if (!inventoryResult.Success)
        {
            return new OrderProcessResult
            {
                OrderId = order.Id,
                Success = false,
                Message = "Priority inventory check failed: " + inventoryResult.Message
            };
        }
        
        // Update order status
        order.Status = "Processing";
        await _dbContext.SaveChangesAsync();
        
        return new OrderProcessResult
        {
            OrderId = order.Id,
            Success = true,
            Message = "Order processed with priority handling",
            Priority = true
        };
    }
    
    private async Task<OrderProcessResult> ProcessOrderStandard(Order order)
    {
        // Standard processing logic
        using var activity = new ActivitySource("OrderProcessing").StartActivity("StandardOrderProcessing");
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("order.customer_id", order.CustomerId);
        activity?.SetTag("order.priority", "standard");
        
        // Standard inventory check
        var inventoryResult = await _inventoryService.CheckAndReserveInventoryAsync(
            order.Id,
            order.ProductId,
            order.Quantity,
            isPriority: false);
            
        if (!inventoryResult.Success)
        {
            return new OrderProcessResult
            {
                OrderId = order.Id,
                Success = false,
                Message = "Standard inventory check failed: " + inventoryResult.Message
            };
        }
        
        // Update order status
        order.Status = "Processing";
        await _dbContext.SaveChangesAsync();
        
        return new OrderProcessResult
        {
            OrderId = order.Id,
            Success = true,
            Message = "Order processed with standard handling",
            Priority = false
        };
    }
    
    // Helper to get customer tier - in a real app this would come from a customer service
    private string GetCustomerTier(int customerId)
    {
        // For this example, customers with ID divisible by 10 are premium
        return customerId % 10 == 0 ? "Premium" : "Standard";
    }
}

/// <summary>
/// Result of order processing
/// </summary>
public class OrderProcessResult
{
    public int OrderId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; }
    public bool Priority { get; set; }
}
