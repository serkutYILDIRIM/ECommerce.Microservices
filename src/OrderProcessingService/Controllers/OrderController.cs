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
        _baggageManager.SetCustomerContext(order.CustomerName, 
            customerTier: GetCustomerTier(order.CustomerName));
            
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
            _baggageManager.SetCustomerContext(order.CustomerName, 
                customerTier: GetCustomerTier(order.CustomerName));
            _baggageManager.SetTransactionContext();
            
            // Capture the business context for the operation
            var context = _contextEnricher.GetBusinessContext();
            
            // Enrich the order with context information
            order = _contextEnricher.EnrichEntity(order);
            order.OrderDate = DateTime.UtcNow;
            order.Status = OrderStatus.Pending;
            
            // Track the operation with business context
            using var activity = new ActivitySource("OrderProcessing").StartActivity("CreateOrder");
            activity?.SetTag("order.customer_name", order.CustomerName);
            activity?.SetTag("order.total", order.TotalAmount);
            activity?.SetTag("business.customer_tier", context.CustomerTier);

            // Save the order
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
            
            // Update baggage with the new order ID
            _baggageManager.SetOrderContext(order.Id.ToString(), order.TotalAmount);
            
            // Return success
            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }
        catch (Exception ex)
        {
            // Log error with business context
            _logger.LogError(ex, "Error creating order for customer {CustomerName}. Context: {Context}", 
                order.CustomerName, _contextEnricher.GetBusinessContext());
            
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
        _baggageManager.SetCustomerContext(order.CustomerName, 
            customerTier: GetCustomerTier(order.CustomerName));
        
        // Get business context from baggage
        var context = _contextEnricher.GetBusinessContext();
        
        // Process with priority handling if needed
        if (context.IsPremiumCustomer)
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
        activity?.SetTag("order.customer_name", order.CustomerName);
        activity?.SetTag("order.priority", "high");
        
        // Update order status
        order.Status = OrderStatus.Processing;
        await _dbContext.SaveChangesAsync();
        
        return new OrderProcessResult
        {
            OrderId = order.Id,
            Success = true,
            Message = "Order processed with priority handling (Inventory check skipped)",
            Priority = true
        };
    }
    
    private async Task<OrderProcessResult> ProcessOrderStandard(Order order)
    {
        // Standard processing logic
        using var activity = new ActivitySource("OrderProcessing").StartActivity("StandardOrderProcessing");
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("order.customer_name", order.CustomerName);
        activity?.SetTag("order.priority", "standard");
        
        // Update order status
        order.Status = OrderStatus.Processing;
        await _dbContext.SaveChangesAsync();
        
        return new OrderProcessResult
        {
            OrderId = order.Id,
            Success = true,
            Message = "Order processed with standard handling (Inventory check skipped)",
            Priority = false
        };
    }
    
    // Helper to get customer tier - changed parameter to string (CustomerName)
    private string GetCustomerTier(string customerName)
    {
        // Example logic: Use customer name length or some other derivable property.
        // This needs to be replaced with actual business logic for determining tier.
        return customerName.Length % 2 == 0 ? "Premium" : "Standard";
    }
}

/// <summary>
/// Result of order processing
/// </summary>
public class OrderProcessResult
{
    public int OrderId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Priority { get; set; }
}
