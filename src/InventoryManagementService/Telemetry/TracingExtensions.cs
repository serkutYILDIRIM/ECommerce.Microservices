using System.Diagnostics;
using InventoryManagementService.Models;

namespace InventoryManagementService.Telemetry;

public static class TracingExtensions
{
    /// <summary>
    /// Creates and starts a new inventory-related span with appropriate attributes
    /// </summary>
    public static Activity? StartInventoryOperation(string operationName, InventoryItem item)
    {
        var activity = TelemetryConfig.ActivitySource.StartActivity(operationName);
        if (activity == null) return null;
        
        // Add common inventory attributes
        activity.SetTag("inventory.id", item.Id);
        activity.SetTag("product.id", item.ProductId);
        activity.SetTag("product.name", item.ProductName);
        activity.SetTag("inventory.quantity_available", item.QuantityAvailable);
        activity.SetTag("inventory.quantity_reserved", item.QuantityReserved);
        activity.SetTag("inventory.location", item.Location);
        
        // Mark the beginning of the operation
        activity.AddEvent(new ActivityEvent($"Started {operationName}"));
        
        return activity;
    }
    
    /// <summary>
    /// Records inventory quantity changes
    /// </summary>
    public static void RecordQuantityChange(this Activity? activity, string changeType, int oldQuantity, int newQuantity, string? reason = null)
    {
        if (activity == null) return;
        
        int delta = newQuantity - oldQuantity;
        
        activity.SetTag($"inventory.{changeType}.old", oldQuantity);
        activity.SetTag($"inventory.{changeType}.new", newQuantity);
        activity.SetTag($"inventory.{changeType}.delta", delta);
        
        if (!string.IsNullOrEmpty(reason))
        {
            activity.SetTag($"inventory.{changeType}.reason", reason);
        }
        
        var tags = new ActivityTagsCollection
        {
            { "change_type", changeType },
            { "old_value", oldQuantity },
            { "new_value", newQuantity },
            { "delta", delta }
        };
        
        if (!string.IsNullOrEmpty(reason))
        {
            tags.Add("reason", reason);
        }
        
        activity.AddEvent(new ActivityEvent("InventoryQuantityChanged", tags: tags));
    }
    
    /// <summary>
    /// Records inventory availability check
    /// </summary>
    public static void RecordAvailabilityCheck(this Activity? activity, int productId, int requestedQuantity, int availableQuantity, bool isAvailable)
    {
        if (activity == null) return;
        
        activity.SetTag("product.id", productId);
        activity.SetTag("quantity.requested", requestedQuantity);
        activity.SetTag("quantity.available", availableQuantity);
        activity.SetTag("inventory.is_available", isAvailable);
        
        var tags = new ActivityTagsCollection
        {
            { "product.id", productId },
            { "quantity.requested", requestedQuantity },
            { "quantity.available", availableQuantity },
            { "is_available", isAvailable }
        };
        
        activity.AddEvent(new ActivityEvent("InventoryAvailabilityChecked", tags: tags));
    }
    
    /// <summary>
    /// Records a restock threshold check
    /// </summary>
    public static void RecordRestockCheck(this Activity? activity, int currentQuantity, int threshold, bool needsRestock)
    {
        if (activity == null) return;
        
        activity.SetTag("inventory.current_quantity", currentQuantity);
        activity.SetTag("inventory.threshold", threshold);
        activity.SetTag("inventory.needs_restock", needsRestock);
        
        if (needsRestock)
        {
            activity.SetTag("inventory.shortage", threshold - currentQuantity);
            activity.AddEvent(new ActivityEvent("RestockNeeded", tags: new ActivityTagsCollection
            {
                { "current_quantity", currentQuantity },
                { "threshold", threshold },
                { "shortage", threshold - currentQuantity }
            }));
        }
    }
}
