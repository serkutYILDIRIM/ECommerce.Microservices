using InventoryManagementService.Data;
using InventoryManagementService.Metrics;
using InventoryManagementService.Models;
using InventoryManagementService.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Shared.Library.Logging;
using System.Diagnostics;

namespace InventoryManagementService.BackgroundServices;

/// <summary>
/// Background service that periodically monitors inventory levels
/// and triggers alerts for low stock items
/// </summary>
public class InventoryMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InventoryMonitoringService> _logger;
    private readonly BackgroundServiceMetrics _metrics;
    private readonly TimeSpan _checkInterval;

    // Track internal state
    private int _executionCount = 0;
    private DateTime _lastExecutionTime = DateTime.MinValue;

    public InventoryMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<InventoryMonitoringService> logger,
        BackgroundServiceMetrics metrics,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _metrics = metrics;

        // Read configuration with default to 1 minute if not specified
        var intervalSeconds = configuration.GetValue<int>("BackgroundServices:InventoryMonitoring:IntervalSeconds", 60);
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation("Inventory monitoring service initialized with check interval of {Interval} seconds",
            _checkInterval.TotalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogWithCategory(LogLevel.Information, CategoryLogger.Categories.BackgroundService,
            "Inventory monitoring service is starting");
        _metrics.RecordServiceStart();

        try
        {
            // Wait a bit before starting to allow the service to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWithCategory(LogLevel.Debug, CategoryLogger.Categories.BackgroundService,
                    "Inventory monitoring cycle starting. Execution count: {Count}", _executionCount + 1);

                var stopwatch = Stopwatch.StartNew();
                bool success = false;
                Exception? error = null;

                using var activity = TelemetryConfig.ActivitySource.StartActivity("InventoryMonitoring.CheckInventoryLevels");

                try
                {
                    await CheckInventoryLevelsAsync(stoppingToken);
                    success = true;
                    _executionCount++;
                    _lastExecutionTime = DateTime.UtcNow;

                    activity?.SetTag("inventory.check.success", true);
                    activity?.SetTag("inventory.check.execution_count", _executionCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    error = ex;
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.RecordException(ex);
                    _logger.LogWithCategory(LogLevel.Error, CategoryLogger.Categories.BackgroundService, ex,
                        "Error checking inventory levels in background service");
                }
                finally
                {
                    stopwatch.Stop();
                    _metrics.RecordExecutionComplete(stopwatch.ElapsedMilliseconds, success, error);

                    activity?.SetTag("inventory.check.duration_ms", stopwatch.ElapsedMilliseconds);

                    _logger.LogWithCategory(LogLevel.Debug, CategoryLogger.Categories.BackgroundService,
                        "Inventory monitoring cycle completed in {ElapsedMs}ms. Success: {Success}",
                        stopwatch.ElapsedMilliseconds, success);
                }

                // Wait for the next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, don't treat as error
            _logger.LogWithCategory(LogLevel.Information, CategoryLogger.Categories.BackgroundService,
                "Inventory monitoring service is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _metrics.RecordServiceFailure(ex);
            _logger.LogWithCategory(LogLevel.Error, CategoryLogger.Categories.BackgroundService, ex,
                "Fatal error in inventory monitoring background service");
            // Re-throw to let the hosting layer know there was a fatal error
            throw;
        }
        finally
        {
            _metrics.RecordServiceStop();
            _logger.LogWithCategory(LogLevel.Information, CategoryLogger.Categories.BackgroundService,
                "Inventory monitoring service has stopped. Total executions: {Count}", _executionCount);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Inventory monitoring service is stopping");
        return base.StopAsync(cancellationToken);
    }

    private async Task CheckInventoryLevelsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // Record the start of a check operation
        _metrics.RecordOperationStart("inventory_check");

        try
        {
            // Find items that are at or below reorder threshold
            var lowStockItems = await dbContext.InventoryItems
                .Where(i => i.QuantityAvailable - i.QuantityReserved <= i.ReorderThreshold)
                .ToListAsync(stoppingToken);

            _metrics.RecordInventoryCheckResults(lowStockItems.Count);

            // Process low stock items
            foreach (var item in lowStockItems)
            {
                try
                {
                    await ProcessLowStockItemAsync(item, dbContext, stoppingToken);
                    _metrics.RecordItemProcessed(item.ProductId, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process low stock item {ProductId}", item.ProductId);
                    _metrics.RecordItemProcessed(item.ProductId, false, ex);
                }
            }

            // Record that the entire operation completed successfully
            _metrics.RecordOperationComplete("inventory_check", true);
        }
        catch (Exception ex)
        {
            _metrics.RecordOperationComplete("inventory_check", false, ex);
            throw; // Re-throw to be handled by the main execution loop
        }
    }

    private async Task ProcessLowStockItemAsync(
        InventoryItem item,
        InventoryDbContext dbContext,
        CancellationToken stoppingToken)
    {
        using var activity = TelemetryConfig.ActivitySource.StartActivity("InventoryMonitoring.ProcessLowStockItem");
        activity?.SetTag("product.id", item.ProductId);
        activity?.SetTag("product.name", item.ProductName);
        activity?.SetTag("inventory.quantity_available", item.QuantityAvailable);
        activity?.SetTag("inventory.quantity_reserved", item.QuantityReserved);
        activity?.SetTag("inventory.reorder_threshold", item.ReorderThreshold);

        var availableForSale = item.QuantityAvailable - item.QuantityReserved;

        // Calculate shortage amount
        var shortage = item.ReorderThreshold - availableForSale;
        if (shortage <= 0)
        {
            // No actual shortage, this could happen if inventory was updated since our query
            activity?.SetTag("inventory.has_shortage", false);
            return;
        }

        activity?.SetTag("inventory.has_shortage", true);
        activity?.SetTag("inventory.shortage_amount", shortage);
        activity?.SetTag("inventory.available_for_sale", availableForSale);

        // Calculate reorder quantity - typically we'd reorder enough to get back to a comfortable level
        // For this example, we'll reorder 2x the threshold
        var reorderQuantity = item.ReorderThreshold * 2;
        activity?.SetTag("inventory.reorder_quantity", reorderQuantity);

        // Record a reorder recommendation
        _metrics.RecordReorderRecommendation(
            item.ProductId,
            item.ProductName,
            availableForSale,
            item.ReorderThreshold,
            reorderQuantity);

        // In a real system, we might create a purchase order or send a notification
        // For this example, we'll just log the recommendation
        _logger.LogWithCategory(LogLevel.Warning, CategoryLogger.Categories.BusinessLogic,
            "Low stock alert: Product {ProductName} (ID: {ProductId}) has only {AvailableQuantity} units available " +
            "(below threshold of {Threshold}). Recommended reorder quantity: {ReorderQuantity}",
            item.ProductName,
            item.ProductId,
            availableForSale,
            item.ReorderThreshold,
            reorderQuantity);

        // Update the inventory item's last checked timestamp
        item.LastUpdated = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);

        // Create a notable event for critical stock levels
        if (availableForSale <= 0)
        {
            activity?.AddEvent(new ActivityEvent("StockoutDetected", tags: new ActivityTagsCollection
            {
                { "product.id", item.ProductId },
                { "product.name", item.ProductName },
                { "severity", "critical" }
            }));

            _metrics.RecordStockoutEvent(item.ProductId, item.ProductName);

            _logger.LogWithCategory(LogLevel.Critical, CategoryLogger.Categories.BusinessLogic,
                "STOCKOUT: Product {ProductName} (ID: {ProductId}) has no available inventory!",
                item.ProductName, item.ProductId);
        }

        // Add a reorder event to the activity
        activity?.AddEvent(new ActivityEvent("ReorderRecommended", tags: new ActivityTagsCollection
        {
            { "product.id", item.ProductId },
            { "product.name", item.ProductName },
            { "quantity.available", availableForSale },
            { "quantity.threshold", item.ReorderThreshold },
            { "quantity.reorder", reorderQuantity }
        }));
    }
}
