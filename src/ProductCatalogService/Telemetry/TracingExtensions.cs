using System.Diagnostics;
using ProductCatalogService.Models;

namespace ProductCatalogService.Telemetry;

public static class TracingExtensions
{
    /// <summary>
    /// Creates and starts a new product-related span with appropriate attributes
    /// </summary>
    public static Activity? StartProductOperation(string operationName, Product product)
    {
        var activity = TelemetryConfig.ActivitySource.StartActivity(operationName);
        if (activity == null) return null;
        
        // Add common product attributes
        activity.SetTag("product.id", product.Id);
        activity.SetTag("product.name", product.Name);
        activity.SetTag("product.category", product.Category);
        activity.SetTag("product.price", product.Price);
        activity.SetTag("product.stock_quantity", product.StockQuantity);
        
        // Mark the beginning of the operation
        activity.AddEvent(new ActivityEvent($"Started {operationName}"));
        
        return activity;
    }
    
    /// <summary>
    /// Records product validation results in the current span
    /// </summary>
    public static void RecordProductValidation(this Activity? activity, bool isValid, List<string>? validationErrors = null)
    {
        if (activity == null) return;
        
        activity.SetTag("product.validation.success", isValid);
        
        if (!isValid && validationErrors != null && validationErrors.Count > 0)
        {
            activity.SetTag("product.validation.error_count", validationErrors.Count);
            
            // Add validation errors as an event with details
            var tags = new ActivityTagsCollection();
            for (int i = 0; i < validationErrors.Count; i++)
            {
                tags[$"error.{i}"] = validationErrors[i];
            }
            
            activity.AddEvent(new ActivityEvent("ProductValidationFailed", tags: tags));
        }
        else
        {
            activity.AddEvent(new ActivityEvent("ProductValidationSucceeded"));
        }
    }
    
    /// <summary>
    /// Records a product search operation with relevant metrics
    /// </summary>
    public static void RecordProductSearch(this Activity? activity, string searchTerm, int resultCount, long executionTimeMs)
    {
        if (activity == null) return;
        
        activity.SetTag("search.term", searchTerm);
        activity.SetTag("search.result_count", resultCount);
        activity.SetTag("search.execution_time_ms", executionTimeMs);
        activity.SetTag("search.found_results", resultCount > 0);
        
        // Record the search as an event
        var tags = new ActivityTagsCollection
        {
            { "search.term", searchTerm },
            { "search.result_count", resultCount },
            { "search.execution_time_ms", executionTimeMs }
        };
        
        activity.AddEvent(new ActivityEvent("ProductSearchCompleted", tags: tags));
    }
}
