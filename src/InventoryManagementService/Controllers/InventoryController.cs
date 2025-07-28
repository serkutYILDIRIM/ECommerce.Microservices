using Microsoft.AspNetCore.Mvc;
using InventoryManagementService.Data;
using InventoryManagementService.Models;
using InventoryManagementService.Services;
using Shared.Library.Telemetry.Baggage;
using Shared.Library.Services;
using System.Diagnostics;

namespace InventoryManagementService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;
    private readonly ILogger<InventoryController> _logger;
    private readonly BaggageManager _baggageManager;
    private readonly BusinessContextEnricher _contextEnricher;
    private readonly IInventoryBusinessRules _businessRules;
    private readonly InventoryReservationQueue _reservationQueue;

    public InventoryController(
        InventoryDbContext dbContext,
        ILogger<InventoryController> logger,
        BaggageManager baggageManager,
        BusinessContextEnricher contextEnricher,
        IInventoryBusinessRules businessRules,
        InventoryReservationQueue reservationQueue)
    {
        _dbContext = dbContext;
        _logger = logger;
        _baggageManager = baggageManager;
        _contextEnricher = contextEnricher;
        _businessRules = businessRules;
        _reservationQueue = reservationQueue;
    }

    [HttpGet("stock/{productId}")]
    public async Task<ActionResult<StockResponse>> GetStock(int productId)
    {
        // Get business context from baggage
        var context = _contextEnricher.GetBusinessContext();
        
        // Start a span to track this operation
        using var activity = new ActivitySource("InventoryManagement").StartActivity("GetStock");
        activity?.SetTag("product.id", productId);
        activity?.SetTag("business.context.customer_tier", context.CustomerTier);
        
        try
        {
            var inventoryItem = await _dbContext.InventoryItems.FindAsync(productId);
            if (inventoryItem == null)
                return NotFound();

            // Extract context for decision making
            var isPremiumCustomer = context.IsPremiumCustomer;
            
            StockResponse response;
            
            // Apply different business rules based on customer tier
            if (isPremiumCustomer)
            {
                // Premium customers get special handling
                response = await _businessRules.GetStockForPremiumCustomer(inventoryItem, context);
                activity?.SetTag("business.rule.applied", "premium_customer");
            }
            else
            {
                // Standard handling
                response = new StockResponse
                {
                    ProductId = inventoryItem.ProductId,
                    QuantityAvailable = inventoryItem.QuantityAvailable,
                    IsInStock = inventoryItem.QuantityAvailable > 0,
                    ReservedQuantity = inventoryItem.QuantityReserved,
                    LastUpdated = inventoryItem.LastUpdated
                };
                activity?.SetTag("business.rule.applied", "standard");
            }
            
            // Add business context to response (for transparency)
            response.CustomerTier = context.CustomerTier;
            response.CorrelationId = context.CorrelationId;
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error getting stock for product {ProductId}, Context: {Context}", 
                productId, context);
            return StatusCode(500, new { message = "An error occurred while checking stock" });
        }
    }

    [HttpPost("reserve")]
    public async Task<ActionResult<InventoryReservationResult>> ReserveInventory(InventoryReservationRequest request)
    {
        // Set transaction context first to ensure correlation
        _baggageManager.SetTransactionContext(correlationId: request.CorrelationId);
        
        // Set order context if provided
        if (request.OrderId > 0)
        {
            _baggageManager.SetOrderContext(request.OrderId.ToString(), 
                priority: request.IsPriority ? "High" : "Standard");
        }
        
        // Get complete business context
        var context = _contextEnricher.GetBusinessContext();
        
        // Start a span to track this operation
        using var activity = new ActivitySource("InventoryManagement").StartActivity("ReserveInventory");
        activity?.SetTag("product.id", request.ProductId);
        activity?.SetTag("order.id", request.OrderId);
        activity?.SetTag("quantity", request.Quantity);
        activity?.SetTag("priority", request.IsPriority);
        activity?.SetTag("business.context.customer_tier", context.CustomerTier);
        
        try
        {
            // Decide whether to process immediately or queue based on baggage context
            if (ShouldProcessImmediately(context, request))
            {
                activity?.SetTag("processing.path", "immediate");
                return await ProcessReservationImmediately(request, context);
            }
            else
            {
                activity?.SetTag("processing.path", "queued");
                return await QueueReservation(request, context);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error reserving inventory for product {ProductId}, Context: {Context}", 
                request.ProductId, context);
            return StatusCode(500, new InventoryReservationResult 
            { 
                Success = false, 
                Message = "An error occurred during inventory reservation"
            });
        }
    }
    
    private bool ShouldProcessImmediately(BusinessContext context, InventoryReservationRequest request)
    {
        // Process immediately if:
        // 1. It's a high priority request or
        // 2. It's from a premium customer or
        // 3. It's a small quantity request (less than 5 items)
        
        bool isPriority = request.IsPriority || context.IsHighPriority;
        bool isPremiumCustomer = context.IsPremiumCustomer;
        bool isSmallQuantity = request.Quantity < 5;
        
        return isPriority || isPremiumCustomer || isSmallQuantity;
    }
    
    private async Task<ActionResult<InventoryReservationResult>> ProcessReservationImmediately(
        InventoryReservationRequest request, BusinessContext context)
    {
        var inventoryItem = await _dbContext.InventoryItems.FindAsync(request.ProductId);
        if (inventoryItem == null)
        {
            return NotFound(new InventoryReservationResult
            {
                Success = false,
                Message = $"Product with ID {request.ProductId} not found"
            });
        }
        
        // Apply business rules to determine if reservation is allowed
        var reservationDecision = _businessRules.EvaluateReservation(inventoryItem, request, context);
        
        if (!reservationDecision.IsAllowed)
        {
            return BadRequest(new InventoryReservationResult
            {
                Success = false,
                Message = reservationDecision.Reason
            });
        }
        
        // Apply reservation
        inventoryItem.QuantityAvailable -= request.Quantity;
        inventoryItem.QuantityReserved += request.Quantity;
        inventoryItem.LastUpdated = DateTime.UtcNow;
        
        // Create reservation record
        var reservation = new InventoryReservation
        {
            ProductId = request.ProductId,
            OrderId = request.OrderId,
            Quantity = request.Quantity,
            ReservationDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddHours(24), // Default 24 hour expiry
            Status = "Active",
            Priority = request.IsPriority || context.IsHighPriority,
            CorrelationId = context.CorrelationId,
            CustomerTier = context.CustomerTier,
            Notes = $"Processed immediately. Context: {(context.IsHighPriority ? "High Priority" : "")} {(context.IsPremiumCustomer ? "Premium Customer" : "")}"
        };
        
        // For premium customers, extend reservation time
        if (context.IsPremiumCustomer)
        {
            reservation.ExpiryDate = DateTime.UtcNow.AddHours(72); // 3 days for premium customers
            reservation.Notes += " Extended expiry applied.";
        }
        
        _dbContext.InventoryReservations.Add(reservation);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation(
            "Inventory reserved for product {ProductId}, order {OrderId}, quantity {Quantity}, priority: {Priority}",
            request.ProductId, request.OrderId, request.Quantity, request.IsPriority);
            
        return Ok(new InventoryReservationResult
        {
            Success = true,
            ReservationId = reservation.Id,
            Message = $"Reservation successful. {(context.IsPremiumCustomer ? "Premium customer benefits applied." : "")}"
        });
    }
    
    private async Task<ActionResult<InventoryReservationResult>> QueueReservation(
        InventoryReservationRequest request, BusinessContext context)
    {
        // Create a reservation task for the queue
        var reservationTask = new InventoryReservationTask
        {
            Request = request,
            Priority = DeterminePriority(context, request),
            SubmittedAt = DateTime.UtcNow,
            BusinessContext = context,
            Status = "Queued"
        };
        
        // Add to the queue with appropriate priority
        var queueTask = _reservationQueue.EnqueueReservation(reservationTask);
        
        // Return immediately with queued status
        return Accepted(new InventoryReservationResult
        {
            Success = true,
            Message = $"Reservation queued for processing. Priority: {reservationTask.Priority}",
            IsQueued = true,
            EstimatedProcessingTime = _reservationQueue.GetEstimatedProcessingTime(reservationTask.Priority)
        });
    }
    
    private int DeterminePriority(BusinessContext context, InventoryReservationRequest request)
    {
        // Higher number = higher priority (0-10 scale)
        int priority = 5; // Default priority
        
        // Boost priority for premium customers
        if (context.IsPremiumCustomer)
            priority += 2;
        
        // Boost priority for high priority orders
        if (request.IsPriority || context.IsHighPriority)
            priority += 3;
        
        // Cap at maximum priority
        return Math.Min(priority, 10);
    }
}

public class StockResponse
{
    public int ProductId { get; set; }
    public int QuantityAvailable { get; set; }
    public bool IsInStock { get; set; }
    public int ReservedQuantity { get; set; }
    public DateTime LastUpdated { get; set; }
    public string CustomerTier { get; set; }
    public string CorrelationId { get; set; }
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
    public bool IsQueued { get; set; }
    public TimeSpan? EstimatedProcessingTime { get; set; }
}
