using System.Diagnostics;
using OrderProcessingService.Models;

namespace OrderProcessingService.Telemetry;

public static class TracingExtensions
{
    /// <summary>
    /// Creates and starts a new order-related span with appropriate attributes
    /// </summary>
    public static Activity? StartOrderOperation(string operationName, Order order)
    {
        var activity = TelemetryConfig.ActivitySource.StartActivity(operationName);
        if (activity == null) return null;
        
        // Add common order attributes
        activity.SetTag("order.id", order.Id);
        activity.SetTag("customer.name", order.CustomerName);
        activity.SetTag("customer.email", order.CustomerEmail);
        activity.SetTag("order.items_count", order.Items.Count);
        activity.SetTag("order.total_amount", order.TotalAmount);
        activity.SetTag("order.status", order.Status.ToString());
        
        // Mark the beginning of the operation
        activity.AddEvent(new ActivityEvent($"Started {operationName}"));
        
        return activity;
    }
    
    /// <summary>
    /// Records the workflow step of an order process
    /// </summary>
    public static void RecordOrderWorkflowStep(this Activity? activity, string stepName, bool success, string? details = null)
    {
        if (activity == null) return;
        
        var tags = new ActivityTagsCollection
        {
            { "workflow.step", stepName },
            { "workflow.success", success }
        };
        
        if (!string.IsNullOrEmpty(details))
        {
            tags.Add("workflow.details", details);
        }
        
        activity.AddEvent(new ActivityEvent($"OrderWorkflow:{stepName}", tags: tags));
    }
    
    /// <summary>
    /// Records an order status change event
    /// </summary>
    public static void RecordOrderStatusChange(this Activity? activity, OrderStatus oldStatus, OrderStatus newStatus, string? reason = null)
    {
        if (activity == null) return;
        
        activity.SetTag("order.status.old", oldStatus.ToString());
        activity.SetTag("order.status.new", newStatus.ToString());
        
        if (!string.IsNullOrEmpty(reason))
        {
            activity.SetTag("order.status.change_reason", reason);
        }
        
        var tags = new ActivityTagsCollection
        {
            { "order.status.old", oldStatus.ToString() },
            { "order.status.new", newStatus.ToString() }
        };
        
        if (!string.IsNullOrEmpty(reason))
        {
            tags.Add("reason", reason);
        }
        
        activity.AddEvent(new ActivityEvent("OrderStatusChanged", tags: tags));
    }
}
