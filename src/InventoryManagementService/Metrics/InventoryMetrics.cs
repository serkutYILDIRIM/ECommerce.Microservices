using System.Diagnostics.Metrics;
using InventoryManagementService.Data;
using InventoryManagementService.Models;
using Shared.Library.Metrics;

namespace InventoryManagementService.Metrics;

/// <summary>
/// Provides metrics for the Inventory Management Service
/// </summary>
public class InventoryMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _inventoryChecksCounter;
    private readonly Counter<long> _inventoryReservationsCounter;
    private readonly Counter<long> _inventoryFulfillmentsCounter;
    private readonly Counter<long> _lowStockEventsCounter;
    private readonly Histogram<double> _reservationProcessingTimeHistogram;
    private readonly Histogram<double> _stockLevelChangeHistogram;
    private readonly ILogger<InventoryMetrics> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory; // Add this field

    // Business metrics
    private readonly Counter<long> _stockoutEventsCounter;
    private readonly Counter<double> _inventoryValueCounter;
    private readonly Histogram<double> _stockTurnoverRateHistogram;
    private readonly Counter<long> _inventoryAdjustmentsCounter;

    public InventoryMetrics(MeterProvider meterProvider, ILogger<InventoryMetrics> logger, IServiceScopeFactory serviceScopeFactory) // Add parameter here
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory; // Store the service scope factory

        // Create counters
        _inventoryChecksCounter = _meter.CreateCounter<long>(
            name: "inventory.checks",
            unit: "{checks}",
            description: "Number of inventory availability checks");

        _inventoryReservationsCounter = _meter.CreateCounter<long>(
            name: "inventory.reservations",
            unit: "{reservations}",
            description: "Number of inventory reservations");

        _inventoryFulfillmentsCounter = _meter.CreateCounter<long>(
            name: "inventory.fulfillments",
            unit: "{fulfillments}",
            description: "Number of inventory fulfillments");

        _lowStockEventsCounter = _meter.CreateCounter<long>(
            name: "inventory.low_stock_events",
            unit: "{events}",
            description: "Number of times inventory reached the reorder threshold");

        // Business metrics
        _stockoutEventsCounter = _meter.CreateCounter<long>(
            name: "business.inventory.stockouts",
            unit: "{events}",
            description: "Number of stockout events (zero available inventory)");

        _inventoryValueCounter = _meter.CreateCounter<double>(
            name: "business.inventory.value",
            unit: "{currency}",
            description: "Total monetary value of inventory");
            
        _stockTurnoverRateHistogram = _meter.CreateHistogram<double>(
            name: "business.inventory.turnover_rate",
            unit: "{ratio}",
            description: "Rate of inventory turnover (sales to average inventory)");
            
        _inventoryAdjustmentsCounter = _meter.CreateCounter<long>(
            name: "business.inventory.adjustments",
            unit: "{units}",
            description: "Number of inventory adjustments (non-sales related)");

        // Create histograms
        _reservationProcessingTimeHistogram = _meter.CreateHistogram<double>(
            name: "inventory.reservation.duration",
            unit: "ms",
            description: "Duration of inventory reservation operations");

        _stockLevelChangeHistogram = _meter.CreateHistogram<double>(
            name: "inventory.stock.level.change",
            unit: "{units}",
            description: "Changes in inventory stock levels");
        // Create observable gauges
        _meter.CreateObservableGauge(
            name: "inventory.total_stock_count",
            observeValue: () => GetTotalStockCount().First().Value,
            unit: "{units}",
            description: "Total units of all inventory items");

        _meter.CreateObservableGauge(
            name: "inventory.reserved_stock_count",
            observeValue: () => GetTotalReservedCount().First().Value,
            unit: "{units}",
            description: "Total reserved units across all inventory items");

        _meter.CreateObservableGauge(
            name: "inventory.low_stock_items_count",
            observeValue: () => GetLowStockItemsCount().First().Value,
            unit: "{items}",
            description: "Number of inventory items with stock at or below reorder threshold");

        // Create an observable gauge for inventory efficiency
        _meter.CreateObservableGauge(
            name: "business.inventory.efficiency",
            observeValue: () => GetInventoryEfficiency().First().Value,
            unit: "{ratio}",
            description: "Ratio of available to reserved inventory");

        // Create an observable gauge for inventory health score
        _meter.CreateObservableGauge(
            name: "business.inventory.health_score",
            observeValue: () => CalculateInventoryHealthScore().First().Value,
            unit: "{score}",
            description: "Overall score of inventory health (0-100)");

        // Create an observable gauge for average days to restock
        _meter.CreateObservableGauge(
            name: "business.inventory.avg_restock_days",
            observeValue: () => GetAverageRestockDays().First().Value,
            unit: "d",
            description: "Average number of days between restocks");

        _logger.LogInformation("Inventory metrics initialized");

    }

    public void RecordInventoryCheck(int productId, int requestedQuantity, bool isAvailable)
    {
        _inventoryChecksCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("quantity.requested", requestedQuantity),
            new KeyValuePair<string, object?>("inventory.is_available", isAvailable));
    }

    public void RecordInventoryReservation(int productId, int quantity, bool success)
    {
        _inventoryReservationsCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("quantity.reserved", quantity),
            new KeyValuePair<string, object?>("reservation.success", success));
    }

    public void RecordInventoryFulfillment(int productId, int quantity, bool success)
    {
        _inventoryFulfillmentsCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("quantity.fulfilled", quantity),
            new KeyValuePair<string, object?>("fulfillment.success", success));
    }

    public void RecordLowStockEvent(InventoryItem item)
    {
        _lowStockEventsCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", item.ProductId),
            new KeyValuePair<string, object?>("product.name", item.ProductName),
            new KeyValuePair<string, object?>("quantity.available", item.QuantityAvailable),
            new KeyValuePair<string, object?>("reorder.threshold", item.ReorderThreshold));
    }

    public void RecordReservationProcessingTime(double durationMs, int productId, int quantity, bool success)
    {
        _reservationProcessingTimeHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("quantity.reserved", quantity),
            new KeyValuePair<string, object?>("reservation.success", success));
    }

    public void RecordStockLevelChange(int productId, int oldQuantity, int newQuantity, string reason)
    {
        int change = newQuantity - oldQuantity;
        _stockLevelChangeHistogram.Record(change,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("change.reason", reason),
            new KeyValuePair<string, object?>("quantity.old", oldQuantity),
            new KeyValuePair<string, object?>("quantity.new", newQuantity));
    }

    public void RecordStockout(InventoryItem item, string reason)
    {
        _stockoutEventsCounter.Add(1,
            new KeyValuePair<string, object?>("product.id", item.ProductId),
            new KeyValuePair<string, object?>("product.name", item.ProductName),
            new KeyValuePair<string, object?>("stockout.reason", reason));
    }

    public void RecordInventoryValue(int productId, string productName, double itemValue, double totalValue)
    {
        _inventoryValueCounter.Add(totalValue,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("product.name", productName),
            new KeyValuePair<string, object?>("item.value", itemValue));
    }

    public void RecordStockTurnoverRate(int productId, string productName, double turnoverRate)
    {
        _stockTurnoverRateHistogram.Record(turnoverRate,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("product.name", productName));
    }

    public void RecordInventoryAdjustment(int productId, string productName, long adjustment, string reason)
    {
        _inventoryAdjustmentsCounter.Add(adjustment,
            new KeyValuePair<string, object?>("product.id", productId),
            new KeyValuePair<string, object?>("product.name", productName),
            new KeyValuePair<string, object?>("adjustment.reason", reason));
    }

    // Observable gauge functions
    private IEnumerable<Measurement<int>> GetTotalStockCount()
    {
        int totalStock = 0;
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            totalStock = dbContext.InventoryItems.Sum(i => i.QuantityAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total stock count for metrics");
            totalStock = 0;
        }

        yield return new Measurement<int>(totalStock);
    }



    private IEnumerable<Measurement<int>> GetTotalReservedCount()
    {
        int totalReserved = 0;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            totalReserved = dbContext.InventoryItems.Sum(i => i.QuantityReserved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total reserved count for metrics");
            totalReserved = 0;
        }

        yield return new Measurement<int>(totalReserved);
    }

    private IEnumerable<Measurement<int>> GetLowStockItemsCount()
    {
        int lowStockCount = 0;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            lowStockCount = dbContext.InventoryItems.Count(i => i.QuantityAvailable <= i.ReorderThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting low stock items count for metrics");
            lowStockCount = 0;
        }

        yield return new Measurement<int>(lowStockCount);
    }

    // Calculate inventory efficiency
    private IEnumerable<Measurement<double>> GetInventoryEfficiency()
    {
        double efficiency = 0;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope(); // Fixed to use _serviceScopeFactory
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var totalAvailable = dbContext.InventoryItems.Sum(i => i.QuantityAvailable);
            var totalReserved = dbContext.InventoryItems.Sum(i => i.QuantityReserved);

            if (totalAvailable > 0)
            {
                efficiency = (double)(totalAvailable - totalReserved) / totalAvailable;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating inventory efficiency");
            efficiency = 0;
        }

        yield return new Measurement<double>(efficiency);
    }

    // Calculate inventory health score
    private IEnumerable<Measurement<double>> CalculateInventoryHealthScore()
    {
        double overallScore = 0;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var inventoryItems = dbContext.InventoryItems.ToList();
            if (inventoryItems.Any())
            {
                foreach (var item in inventoryItems)
                {
                    // Check if stock is above threshold
                    double thresholdScore = item.QuantityAvailable > item.ReorderThreshold ? 100 :
                        (double)item.QuantityAvailable / item.ReorderThreshold * 100;

                    // Check reservation ratio
                    double reservationRatio = item.QuantityAvailable == 0 ? 0 :
                        (double)(item.QuantityAvailable - item.QuantityReserved) / item.QuantityAvailable;
                    double reservationScore = reservationRatio * 100;

                    // Calculate item score (weighted average)
                    double itemScore = (thresholdScore * 0.7) + (reservationScore * 0.3);
                    overallScore += itemScore;
                }

                // Average score across all items
                overallScore /= inventoryItems.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating inventory health score");
            overallScore = 0;
        }

        yield return new Measurement<double>(overallScore);
    }

    // Calculate average days to restock
    private IEnumerable<Measurement<double>> GetAverageRestockDays()
    {
        double avgDays = 0;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var now = DateTime.UtcNow;
            var items = dbContext.InventoryItems.Where(i => i.LastRestocked != default).ToList();

            if (items.Any())
            {
                avgDays = items.Average(i => (now - i.LastRestocked).TotalDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating average restock days");
            avgDays = 0;
        }

        yield return new Measurement<double>(avgDays);
    }

}
