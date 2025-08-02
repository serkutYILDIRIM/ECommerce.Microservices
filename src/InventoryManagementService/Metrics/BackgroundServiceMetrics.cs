using System.Diagnostics;
using System.Diagnostics.Metrics;
using Shared.Library.Metrics;

namespace InventoryManagementService.Metrics;

/// <summary>
/// Provides metrics for background service operations
/// </summary>
public class BackgroundServiceMetrics
{
    private readonly Meter _meter;
    private readonly ILogger<BackgroundServiceMetrics> _logger;
    
    // Service lifecycle metrics
    private readonly Counter<long> _serviceStartCounter;
    private readonly Counter<long> _serviceStopCounter;
    private readonly Counter<long> _serviceFailureCounter;
    
    // Execution metrics
    private readonly Counter<long> _executionCounter;
    private readonly Counter<long> _failedExecutionCounter;
    private readonly Histogram<double> _executionDurationHistogram;
    
    // Operation metrics
    private readonly Counter<long> _operationCounter;
    private readonly Counter<long> _operationFailureCounter;
    private readonly Histogram<double> _operationDurationHistogram;
    
    // Business metrics
    private readonly Counter<long> _inventoryChecksCounter;
    private readonly Counter<long> _lowStockItemsCounter;
    private readonly Counter<long> _reorderRecommendationsCounter;
    private readonly Counter<long> _stockoutEventsCounter;
    private readonly Counter<long> _itemProcessedCounter;
    private readonly Counter<long> _itemProcessingFailureCounter;
    
    // Internal state tracking
    private readonly Dictionary<string, Stopwatch> _operationStopwatches = new();
    private readonly object _lock = new();
    
    // Observable metrics
    private bool _isRunning = false;
    private DateTime _startTime = DateTime.MinValue;
    private DateTime _lastSuccessfulExecution = DateTime.MinValue;
    private DateTime _lastExecutionAttempt = DateTime.MinValue;
    private long _totalExecutions = 0;
    private long _totalFailedExecutions = 0;
    private long _consecutiveFailures = 0;

    public BackgroundServiceMetrics(MeterProvider meterProvider, ILogger<BackgroundServiceMetrics> logger)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;
        
        // Service lifecycle metrics
        _serviceStartCounter = _meter.CreateCounter<long>(
            name: "background_service.starts",
            unit: "{starts}",
            description: "Number of times the background service has started");
            
        _serviceStopCounter = _meter.CreateCounter<long>(
            name: "background_service.stops",
            unit: "{stops}",
            description: "Number of times the background service has stopped");
            
        _serviceFailureCounter = _meter.CreateCounter<long>(
            name: "background_service.failures",
            unit: "{failures}",
            description: "Number of fatal errors in the background service");
        
        // Execution metrics
        _executionCounter = _meter.CreateCounter<long>(
            name: "background_service.executions",
            unit: "{executions}",
            description: "Number of background task executions");
            
        _failedExecutionCounter = _meter.CreateCounter<long>(
            name: "background_service.failed_executions",
            unit: "{failures}",
            description: "Number of failed background task executions");
            
        _executionDurationHistogram = _meter.CreateHistogram<double>(
            name: "background_service.execution_duration",
            unit: "ms",
            description: "Duration of background task executions");
        
        // Operation metrics
        _operationCounter = _meter.CreateCounter<long>(
            name: "background_service.operations",
            unit: "{operations}",
            description: "Number of operations performed by background service");
            
        _operationFailureCounter = _meter.CreateCounter<long>(
            name: "background_service.operation_failures",
            unit: "{failures}",
            description: "Number of failed operations in background service");
            
        _operationDurationHistogram = _meter.CreateHistogram<double>(
            name: "background_service.operation_duration",
            unit: "ms",
            description: "Duration of operations performed by background service");
        
        // Business metrics
        _inventoryChecksCounter = _meter.CreateCounter<long>(
            name: "background_service.inventory_checks",
            unit: "{checks}",
            description: "Number of inventory checks performed");
            
        _lowStockItemsCounter = _meter.CreateCounter<long>(
            name: "background_service.low_stock_items",
            unit: "{items}",
            description: "Number of low stock items detected");
            
        _reorderRecommendationsCounter = _meter.CreateCounter<long>(
            name: "background_service.reorder_recommendations",
            unit: "{recommendations}",
            description: "Number of reorder recommendations generated");
            
        _stockoutEventsCounter = _meter.CreateCounter<long>(
            name: "background_service.stockout_events",
            unit: "{events}",
            description: "Number of stockout events detected");
            
        _itemProcessedCounter = _meter.CreateCounter<long>(
            name: "background_service.items_processed",
            unit: "{items}",
            description: "Number of items processed by the background service");
            
        _itemProcessingFailureCounter = _meter.CreateCounter<long>(
            name: "background_service.item_processing_failures",
            unit: "{failures}",
            description: "Number of item processing failures");

        // Observable metrics
        _meter.CreateObservableGauge(
            name: "background_service.is_running",
            observeValue: () => _isRunning ? 1 : 0,
            unit: "{state}",
            description: "Whether the background service is currently running");

        _meter.CreateObservableGauge(
            name: "background_service.uptime",
            observeValue: () =>
            {
                if (!_isRunning || _startTime == DateTime.MinValue)
                    return 0;

                return (DateTime.UtcNow - _startTime).TotalSeconds;
            },
            unit: "s",
            description: "Uptime of the background service in seconds");

        _meter.CreateObservableGauge(
            name: "background_service.time_since_last_execution",
            observeValue: () =>
            {
                if (_lastSuccessfulExecution == DateTime.MinValue)
                    return 0;

                return (DateTime.UtcNow - _lastSuccessfulExecution).TotalSeconds;
            },
            unit: "s",
            description: "Time since last successful execution in seconds");

        _meter.CreateObservableGauge(
            name: "background_service.consecutive_failures",
            observeValue: () => _consecutiveFailures,
            unit: "{failures}",
            description: "Number of consecutive execution failures");

        _meter.CreateObservableGauge(
            name: "background_service.health_score",
            observeValue: () => CalculateHealthScore(),
            unit: "{score}",
            description: "Health score of the background service (0-100)");

        _logger.LogInformation("Background service metrics initialized");
    }

    #region Service Lifecycle

    public void RecordServiceStart()
    {
        _serviceStartCounter.Add(1);
        _isRunning = true;
        _startTime = DateTime.UtcNow;
        _consecutiveFailures = 0;
        _logger.LogDebug("Background service started");
    }
    
    public void RecordServiceStop()
    {
        _serviceStopCounter.Add(1);
        _isRunning = false;
        _logger.LogDebug("Background service stopped");
    }
    
    public void RecordServiceFailure(Exception ex)
    {
        _serviceFailureCounter.Add(1, 
            new KeyValuePair<string, object?>("error.type", ex.GetType().Name));
        _isRunning = false;
        _logger.LogError(ex, "Background service failed");
    }

    #endregion
    
    #region Execution Tracking
    
    public void RecordExecutionComplete(double durationMs, bool success, Exception? error = null)
    {
        _lastExecutionAttempt = DateTime.UtcNow;
        _totalExecutions++;
        
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("execution.success", success)
        };
        
        if (success)
        {
            _executionCounter.Add(1, tags.ToArray());
            _lastSuccessfulExecution = DateTime.UtcNow;
            _consecutiveFailures = 0;
        }
        else
        {
            _totalFailedExecutions++;
            _consecutiveFailures++;
            
            if (error != null)
            {
                tags.Add(new KeyValuePair<string, object?>("error.type", error.GetType().Name));
            }
            
            _failedExecutionCounter.Add(1, tags.ToArray());
        }
        
        _executionDurationHistogram.Record(durationMs, tags.ToArray());
        
        _logger.LogDebug("Recorded execution completion. Success: {Success}, Duration: {Duration}ms", 
            success, durationMs);
    }
    
    #endregion
    
    #region Operation Tracking
    
    public void RecordOperationStart(string operationName)
    {
        var stopwatch = Stopwatch.StartNew();
        
        lock (_lock)
        {
            _operationStopwatches[operationName] = stopwatch;
        }
        
        _logger.LogTrace("Operation started: {OperationName}", operationName);
    }
    
    public void RecordOperationComplete(string operationName, bool success, Exception? error = null)
    {
        Stopwatch? stopwatch;
        
        lock (_lock)
        {
            if (!_operationStopwatches.TryGetValue(operationName, out stopwatch))
            {
                _logger.LogWarning("Attempted to complete operation {OperationName} that was not started", operationName);
                return;
            }
            
            _operationStopwatches.Remove(operationName);
        }
        
        stopwatch.Stop();
        
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("operation.name", operationName),
            new KeyValuePair<string, object?>("operation.success", success)
        };
        
        if (success)
        {
            _operationCounter.Add(1, tags.ToArray());
        }
        else
        {
            if (error != null)
                tags.Add(new KeyValuePair<string, object?>("error.type", error.GetType().Name));
            
            
            _operationFailureCounter.Add(1, tags.ToArray());
        }
        
        _operationDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags.ToArray());
        
        _logger.LogTrace("Operation completed: {OperationName}, Success: {Success}, Duration: {Duration}ms", 
            operationName, success, stopwatch.ElapsedMilliseconds);
    }
    
    #endregion
    
    #region Business Metrics
    
    public void RecordInventoryCheckResults(int lowStockItemCount)
    {
        _inventoryChecksCounter.Add(1);
        _lowStockItemsCounter.Add(lowStockItemCount);
        
        _logger.LogInformation("Inventory check completed. Found {Count} items with low stock", lowStockItemCount);
    }
    
    public void RecordReorderRecommendation(
        int productId, 
        string productName, 
        int currentStock, 
        int threshold, 
        int recommendedQuantity)
    {
        _reorderRecommendationsCounter.Add(1, 
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("product.name", productName),
            new KeyValuePair<string, object?>("inventory.current_stock", currentStock),
            new KeyValuePair<string, object?>("inventory.threshold", threshold),
            new KeyValuePair<string, object?>("inventory.recommended_quantity", recommendedQuantity));
            
        _logger.LogInformation(
            "Reorder recommendation generated for {ProductName} (ID: {ProductId}). " +
            "Current: {CurrentStock}, Threshold: {Threshold}, Recommended: {RecommendedQuantity}",
            productName, productId, currentStock, threshold, recommendedQuantity);
    }
    
    public void RecordStockoutEvent(int productId, string productName)
    {
        _stockoutEventsCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("product.name", productName));
            
        _logger.LogWarning("Stockout detected for {ProductName} (ID: {ProductId})", productName, productId);
    }
    
    public void RecordItemProcessed(int productId, bool success, Exception? error = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("processing.success", success)
        };
        
        if (success)
        {
            _itemProcessedCounter.Add(1, tags.ToArray());
        }
        else
        {
            if (error != null)
                tags.Add(new KeyValuePair<string, object?>("error.type", error.GetType().Name));
            
            _itemProcessingFailureCounter.Add(1, tags.ToArray());
        }
    }

    #endregion

    #region Health Score

    private double CalculateHealthScore()
    {
        try
        {
            double score = 100;

            // If service isn't running, score is 0
            if (!_isRunning)
                return 0;

            // If we've never had a successful execution, score is low
            if (_lastSuccessfulExecution == DateTime.MinValue)
                return 10;

            // Deduct points for consecutive failures
            if (_consecutiveFailures > 0)
            {
                // Deduct 10 points for each consecutive failure, up to 50 points
                score -= Math.Min(50, _consecutiveFailures * 10);
            }

            // Deduct points for high failure rate
            if (_totalExecutions > 0)
            {
                double failureRate = (double)_totalFailedExecutions / _totalExecutions;
                score -= failureRate * 30; // Deduct up to 30 points for high failure rate
            }

            // Deduct points for staleness (no recent successful execution)
            var timeSinceLastSuccess = DateTime.UtcNow - _lastSuccessfulExecution;
            
            if (timeSinceLastSuccess.TotalMinutes > 5)
                score -= Math.Min(20, timeSinceLastSuccess.TotalMinutes); // Deduct up to 20 points for staleness

            // Ensure score is between 0 and 100
            score = Math.Max(0, Math.Min(100, score));

            return score;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating health score");
            return 0;
        }
    }


    #endregion
}
