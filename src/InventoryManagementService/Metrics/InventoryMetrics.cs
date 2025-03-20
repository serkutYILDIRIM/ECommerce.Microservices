using System.Diagnostics.Metrics;
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

    // Business metrics
    private readonly Counter<long> _stockoutEventsCounter;
    private readonly Counter<double> _inventoryValueCounter;
    private readonly Histogram<double> _stockTurnoverRateHistogram;
    private readonly Counter<long> _inventoryAdjustmentsCounter;

    public InventoryMetrics(MeterProvider meterProvider, ILogger<InventoryMetrics> logger)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;

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
            observeValue: GetTotalStockCount,
            unit: "{units}",
            description: "Total units of all inventory items");

        _meter.CreateObservableGauge(
            name: "inventory.reserved_stock_count",
            observeValue: GetTotalReservedCount,
            unit: "{units}",
            description: "Total reserved units across all inventory items");

        _meter.CreateObservableGauge(
            name: "inventory.low_stock_items_count",
            observeValue: GetLowStockItemsCount,
            unit: "{items}",
            description: "Number of inventory items with stock at or below reorder threshold");

        // Create an observable gauge for inventory efficiency
        _meter.CreateObservableGauge(
            name: "business.inventory.efficiency",
            observeValue: GetInventoryEfficiency,
            unit: "{ratio}",
            description: "Ratio of available to reserved inventory");
            
        // Create an observable gauge for inventory health score
        _meter.CreateObservableGauge(
            name: "business.inventory.health_score",
            observeValue: CalculateInventoryHealthScore,
            unit: "{score}",
            description: "Overall score of inventory health (0-100)");
            
        // Create an observable gauge for average days to restock
        _meter.CreateObservableGauge(
            name: "business.inventory.avg_restock_days",
            observeValue: GetAverageRestockDays,
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
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var totalStock = dbContext.Inventory.Sum(i => i.QuantityAvailable);
            
            yield return new Measurement<int>(totalStock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total stock count for metrics");
            yield return new Measurement<int>(0);
        }
    }

    private IEnumerable<Measurement<int>> GetTotalReservedCount()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var totalReserved = dbContext.Inventory.Sum(i => i.QuantityReserved);
            
            yield return new Measurement<int>(totalReserved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total reserved count for metrics");
            yield return new Measurement<int>(0);
        }
    }

    private IEnumerable<Measurement<int>> GetLowStockItemsCount()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var lowStockCount = dbContext.Inventory.Count(i => i.QuantityAvailable <= i.ReorderThreshold);
            
            yield return new Measurement<int>(lowStockCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting low stock items count for metrics");
            yield return new Measurement<int>(0);
        }
    }

    // Calculate inventory efficiency
    private IEnumerable<Measurement<double>> GetInventoryEfficiency()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            
            var totalAvailable = dbContext.Inventory.Sum(i => i.QuantityAvailable);
            var totalReserved = dbContext.Inventory.Sum(i => i.QuantityReserved);
            
            if (totalAvailable == 0)
                yield return new Measurement<double>(0);
            else
            {
                var efficiency = (double)(totalAvailable - totalReserved) / totalAvailable;
                yield return new Measurement<double>(efficiency);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating inventory efficiency");
            yield return new Measurement<double>(0);
        }
    }

    // Calculate inventory health score
    private IEnumerable<Measurement<double>> CalculateInventoryHealthScore()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            
            var inventoryItems = dbContext.Inventory.ToList();
            if (!inventoryItems.Any())
                yield return new Measurement<double>(0);
            else
            {
                double overallScore = 0;
                
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
                yield return new Measurement<double>(overallScore);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating inventory health score");
            yield return new Measurement<double>(0);
        }
    }

    // Calculate average days to restock
    private IEnumerable<Measurement<double>> GetAverageRestockDays()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            
            var now = DateTime.UtcNow;
            var items = dbContext.Inventory.Where(i => i.LastRestocked != default).ToList();
            
            if (!items.Any())
                yield return new Measurement<double>(0);
            else
            {
                var avgDays = items.Average(i => (now - i.LastRestocked).TotalDays);
                yield return new Measurement<double>(avgDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating average restock days");
            yield return new Measurement<double>(0);
        }
    }
}
