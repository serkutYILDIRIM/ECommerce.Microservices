using InventoryManagementService.Controllers;
using InventoryManagementService.Models;
using Shared.Library.Services;
using Shared.Library.Telemetry.Baggage;
using System.Diagnostics;

namespace InventoryManagementService.Services;

/// <summary>
/// Defines business rules for inventory operations
/// </summary>
public interface IInventoryBusinessRules
{
    /// <summary>
    /// Evaluates a reservation request to determine if it's allowed
    /// </summary>
    ReservationDecision EvaluateReservation(InventoryItem item, InventoryReservationRequest request, BusinessContext context);
    
    /// <summary>
    /// Gets stock information with special handling for premium customers
    /// </summary>
    Task<StockResponse> GetStockForPremiumCustomer(InventoryItem item, BusinessContext context);
    
    /// <summary>
    /// Applies business discounts and special handling based on customer tier
    /// </summary>
    Task ApplyBusinessRules(InventoryReservation reservation, BusinessContext context);
}

/// <summary>
/// Implementation of inventory business rules with baggage-based decision making
/// </summary>
public class InventoryBusinessRules : IInventoryBusinessRules
{
    private readonly ILogger<InventoryBusinessRules> _logger;
    private readonly BaggageManager _baggageManager;
    
    // Configuration for business rules
    private readonly Dictionary<string, int> _tierReservationLimits = new()
    {
        { "premium", 100 },
        { "gold", 50 },
        { "standard", 20 }
    };
    
    // Reserved stock allocation by tier (as percentage of total)
    private readonly Dictionary<string, int> _reservedStockAllocation = new()
    {
        { "premium", 20 }, // Premium customers can access 20% of reserved stock
        { "gold", 10 },    // Gold customers can access 10% of reserved stock
        { "standard", 0 }  // Standard customers cannot access reserved stock
    };

    public InventoryBusinessRules(ILogger<InventoryBusinessRules> logger, BaggageManager baggageManager)
    {
        _logger = logger;
        _baggageManager = baggageManager;
    }

    /// <summary>
    /// Evaluates if a reservation request should be allowed based on business rules
    /// </summary>
    public ReservationDecision EvaluateReservation(
        InventoryItem item, 
        InventoryReservationRequest request, 
        BusinessContext context)
    {
        // Track activity with business context
        using var activity = new ActivitySource("InventoryBusinessRules").StartActivity("EvaluateReservation");
        activity?.SetTag("product.id", item.ProductId);
        activity?.SetTag("request.quantity", request.Quantity);
        activity?.SetTag("customer.tier", context.CustomerTier ?? "unknown");
        activity?.SetTag("priority", context.IsHighPriority);
        
        // Get maximum allowed reservation based on customer tier
        var maxReservationQuantity = GetMaxReservationQuantity(context.CustomerTier);
        
        // Check if requested quantity exceeds the customer tier limit
        if (request.Quantity > maxReservationQuantity)
        {
            var message = $"Requested quantity ({request.Quantity}) exceeds the maximum allowed for {context.CustomerTier ?? "standard"} tier ({maxReservationQuantity})";
            activity?.SetTag("decision.allowed", false);
            activity?.SetTag("decision.reason", "quantity_limit_exceeded");
            
            return new ReservationDecision
            {
                IsAllowed = false,
                Reason = message
            };
        }
        
        // Calculate available stock including reserved allocation based on customer tier
        int effectiveAvailableStock = CalculateEffectiveAvailableStock(item, context.CustomerTier);
        
        // Check if there's enough effective stock
        if (request.Quantity > effectiveAvailableStock)
        {
            var message = $"Insufficient stock. Requested: {request.Quantity}, Available: {effectiveAvailableStock}";
            activity?.SetTag("decision.allowed", false);
            activity?.SetTag("decision.reason", "insufficient_stock");
            
            // Special case: If the customer is premium and we're close to having enough stock,
            // we can allow a small backorder for them
            if (context.IsPremiumCustomer && 
                effectiveAvailableStock > 0 && 
                request.Quantity <= effectiveAvailableStock * 1.2) // Allow up to 20% more for premium
            {
                activity?.SetTag("decision.allowed", true);
                activity?.SetTag("decision.reason", "premium_backorder_allowed");
                
                return new ReservationDecision
                {
                    IsAllowed = true,
                    Reason = "Premium customer backorder allowed",
                    BackOrderRequired = true
                };
            }
            
            return new ReservationDecision
            {
                IsAllowed = false,
                Reason = message
            };
        }
        
        // All checks passed, reservation is allowed
        activity?.SetTag("decision.allowed", true);
        activity?.SetTag("decision.reason", "all_checks_passed");
        
        return new ReservationDecision
        {
            IsAllowed = true,
            Reason = "Reservation allowed"
        };
    }

    /// <summary>
    /// Gets stock information with enhanced availability for premium customers
    /// </summary>
    public async Task<StockResponse> GetStockForPremiumCustomer(InventoryItem item, BusinessContext context)
    {
        // For premium customers, we include reserved stock allocations
        int premiumAllocation = CalculatePremiumAllocation(item);
        
        var response = new StockResponse
        {
            ProductId = item.ProductId,
            QuantityAvailable = item.QuantityAvailable + premiumAllocation,
            IsInStock = item.QuantityAvailable + premiumAllocation > 0,
            ReservedQuantity = item.QuantityReserved - premiumAllocation, // Adjusted for visibility
            LastUpdated = item.LastUpdated,
            CustomerTier = context.CustomerTier
        };
        
        // Add note about premium allocation
        if (premiumAllocation > 0)
        {
            _logger.LogInformation(
                "Premium allocation applied for product {ProductId}: {Allocation} units from reserved stock",
                item.ProductId, premiumAllocation);
        }
        
        return response;
    }

    /// <summary>
    /// Applies business rules like extended reservation times based on customer tier
    /// </summary>
    public async Task ApplyBusinessRules(InventoryReservation reservation, BusinessContext context)
    {
        // Apply different rules based on customer tier
        if (context.IsPremiumCustomer)
        {
            // Premium customers get longer reservation times
            reservation.ExpiryDate = DateTime.UtcNow.AddHours(72); // 3 days
            reservation.Notes += " Premium benefits applied: Extended reservation time.";
        }
        else if (context.CustomerTier?.Equals("gold", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Gold customers get standard+ reservation times
            reservation.ExpiryDate = DateTime.UtcNow.AddHours(48); // 2 days
            reservation.Notes += " Gold benefits applied: Extended reservation time.";
        }
        else
        {
            // Standard customers get basic reservation time
            reservation.ExpiryDate = DateTime.UtcNow.AddHours(24); // 1 day
        }
        
        // Apply priority flag based on context
        if (context.IsHighPriority)
        {
            reservation.Priority = true;
            reservation.Notes += " High priority handling applied.";
        }
        
        // Apply any custom business logic from baggage
        ApplyCustomRulesFromBaggage(reservation, context);
    }
    
    /// <summary>
    /// Calculates how much reserved stock should be made available to premium customers
    /// </summary>
    private int CalculatePremiumAllocation(InventoryItem item)
    {
        // If there's no reserved stock, no allocation
        if (item.QuantityReserved <= 0)
            return 0;
            
        // Get percentage allocation based on tier
        string customerTier = _baggageManager.Get(BaggageManager.Keys.CustomerTier)?.ToLowerInvariant() ?? "standard";
        int allocationPercentage = _reservedStockAllocation.TryGetValue(customerTier, out var percentage) 
            ? percentage 
            : 0;
            
        // Calculate the allocation
        return (int)Math.Ceiling(item.QuantityReserved * allocationPercentage / 100.0);
    }
    
    /// <summary>
    /// Gets maximum reservation quantity based on customer tier
    /// </summary>
    private int GetMaxReservationQuantity(string customerTier)
    {
        string tier = customerTier?.ToLowerInvariant() ?? "standard";
        return _tierReservationLimits.TryGetValue(tier, out var limit) ? limit : 20; // Default to standard
    }
    
    /// <summary>
    /// Calculates effective available stock including tier-based reserved allocation
    /// </summary>
    private int CalculateEffectiveAvailableStock(InventoryItem item, string customerTier)
    {
        // Start with available stock
        int effectiveStock = item.QuantityAvailable;
        
        // Add any reserved stock allocation based on tier
        effectiveStock += CalculatePremiumAllocation(item);
        
        return effectiveStock;
    }
    
    /// <summary>
    /// Applies custom business rules based on baggage data
    /// </summary>
    private void ApplyCustomRulesFromBaggage(InventoryReservation reservation, BusinessContext context)
    {
        // Apply channel-specific rules
        if (!string.IsNullOrEmpty(context.Channel))
        {
            if (context.Channel.Equals("mobile_app", StringComparison.OrdinalIgnoreCase))
                reservation.Notes += " Mobile app order."; // Apply mobile app promotion if applicable

            else if (context.Channel.Equals("partner_api", StringComparison.OrdinalIgnoreCase))
            {
                // Apply special partner handling
                reservation.Notes += " Partner API order.";
            }
        }
        
        // Apply region-specific rules
        if (!string.IsNullOrEmpty(context.Region))
        {
            // Different regions might have special handling
            switch (context.Region.ToLowerInvariant())
            {
                case "europe":
                    reservation.Notes += " EU order handling applied.";
                    break;
                case "asia":
                    reservation.Notes += " Asia order handling applied.";
                    break;
            }
        }
    }
}

/// <summary>
/// Represents a decision about whether a reservation is allowed
/// </summary>
public class ReservationDecision
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; }
    public bool BackOrderRequired { get; set; }
}
