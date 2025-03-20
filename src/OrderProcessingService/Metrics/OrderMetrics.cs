using System.Diagnostics.Metrics;
using OrderProcessingService.Models;
using Shared.Library.Metrics;

namespace OrderProcessingService.Metrics;

/// <summary>
/// Provides metrics for the Order Processing Service
/// </summary>
public class OrderMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _orderCreationsCounter;
    private readonly Counter<long> _orderStatusChangesCounter;
    private readonly Histogram<double> _orderProcessingDurationHistogram;
    private readonly Dictionary<string, ObservableGauge<int>> _orderStatusGauges = new();
    private readonly ILogger<OrderMetrics> _logger;
    private readonly object _lock = new();

    // Business specific metrics
    private readonly Counter<long> _orderValueCounter;
    private readonly Histogram<double> _orderValueHistogram;
    private readonly Counter<long> _orderItemsCounter;
    private readonly Counter<long> _orderCancellationsCounter;
    private readonly Dictionary<string, Counter<long>> _ordersByCategory = new();

    public OrderMetrics(MeterProvider meterProvider, ILogger<OrderMetrics> logger)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;

        // Create counters
        _orderCreationsCounter = _meter.CreateCounter<long>(
            name: "order.creations",
            unit: "{orders}",
            description: "Number of orders created");

        _orderStatusChangesCounter = _meter.CreateCounter<long>(
            name: "order.status.changes",
            unit: "{changes}",
            description: "Number of order status changes");

        // Create a histogram for order processing time
        _orderProcessingDurationHistogram = _meter.CreateHistogram<double>(
            name: "order.processing.duration",
            unit: "ms",
            description: "Duration of order processing operations");

        // Create an observable gauge for total order count
        _meter.CreateObservableGauge(
            name: "order.count.total",
            observeValue: GetTotalOrderCount,
            unit: "{orders}",
            description: "Total number of orders in the system");

        // Business metrics
        _orderValueCounter = _meter.CreateCounter<long>(
            name: "business.order.total_value",
            unit: "{currency}",
            description: "Total monetary value of orders placed");

        _orderValueHistogram = _meter.CreateHistogram<double>(
            name: "business.order.value_distribution",
            unit: "{currency}",
            description: "Distribution of order values");
            
        _orderItemsCounter = _meter.CreateCounter<long>(
            name: "business.order.items_sold",
            unit: "{items}",
            description: "Total number of items sold");
            
        _orderCancellationsCounter = _meter.CreateCounter<long>(
            name: "business.order.cancellations",
            unit: "{orders}",
            description: "Number of cancelled orders");

        // Create an observable gauge for average order processing time
        _meter.CreateObservableGauge(
            name: "business.order.avg_processing_time",
            observeValue: GetAverageProcessingTime,
            unit: "ms",
            description: "Average time to process an order");
            
        // Create an observable gauge for order conversion rate
        _meter.CreateObservableGauge(
            name: "business.order.conversion_rate",
            observeValue: GetOrderConversionRate,
            unit: "{percent}",
            description: "Ratio of completed to cancelled orders");
            
        // Create an observable gauge for orders awaiting fulfillment
        _meter.CreateObservableGauge(
            name: "business.order.awaiting_fulfillment",
            observeValue: GetOrdersAwaitingFulfillment,
            unit: "{orders}",
            description: "Number of orders waiting to be fulfilled");

        // Set up initial status gauges for all possible statuses
        foreach (OrderStatus status in Enum.GetValues(typeof(OrderStatus)))
        {
            EnsureStatusGaugeExists(status.ToString());
        }

        _logger.LogInformation("Order metrics initialized");
    }

    public void RecordOrderCreation(Order order)
    {
        _orderCreationsCounter.Add(1, 
            new KeyValuePair<string, object?>("customer.name", order.CustomerName),
            new KeyValuePair<string, object?>("order.items_count", order.Items.Count),
            new KeyValuePair<string, object?>("order.status", order.Status.ToString()));

        // Record business metrics
        var orderValue = (long)order.TotalAmount;
        _orderValueCounter.Add(orderValue);
        _orderValueHistogram.Record(Convert.ToDouble(order.TotalAmount));
        
        // Count total number of items sold
        var totalItems = order.Items.Sum(i => i.Quantity);
        _orderItemsCounter.Add(totalItems);
        
        // Track orders by category
        foreach (var item in order.Items)
        {
            RecordOrderByCategory(item.ProductName, item.Quantity, (long)(item.UnitPrice * item.Quantity));
        }
    }

    public void RecordOrderStatusChange(int orderId, OrderStatus oldStatus, OrderStatus newStatus)
    {
        _orderStatusChangesCounter.Add(1,
            new KeyValuePair<string, object?>("order.id", orderId),
            new KeyValuePair<string, object?>("order.status.old", oldStatus.ToString()),
            new KeyValuePair<string, object?>("order.status.new", newStatus.ToString()));

        // Track cancellations as a business metric
        if (newStatus == OrderStatus.Cancelled)
        {
            _orderCancellationsCounter.Add(1, 
                new KeyValuePair<string, object?>("order.id", orderId),
                new KeyValuePair<string, object?>("order.previous_status", oldStatus.ToString()));
        }
    }

    public void RecordOrderProcessingDuration(double durationMs, Order order, bool success)
    {
        _orderProcessingDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("order.id", order.Id),
            new KeyValuePair<string, object?>("order.items_count", order.Items.Count),
            new KeyValuePair<string, object?>("order.status", order.Status.ToString()),
            new KeyValuePair<string, object?>("order.processing.success", success));
    }

    // Observable gauge functions must return an IEnumerable<Measurement<T>>
    private IEnumerable<Measurement<int>> GetTotalOrderCount()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var count = dbContext.Orders.Count();
            
            yield return new Measurement<int>(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total order count for metrics");
            yield return new Measurement<int>(0);
        }
    }

    private void EnsureStatusGaugeExists(string status)
    {
        if (string.IsNullOrEmpty(status)) return;
        
        lock (_lock)
        {
            if (!_orderStatusGauges.ContainsKey(status))
            {
                var gauge = _meter.CreateObservableGauge(
                    name: $"order.count.by.status",
                    observeValue: () => GetOrderCountByStatus(status),
                    unit: "{orders}",
                    description: $"Number of orders with status: {status}");
                
                _orderStatusGauges[status] = gauge;
                _logger.LogInformation("Created order count gauge for status {Status}", status);
            }
        }
    }

    private IEnumerable<Measurement<int>> GetOrderCountByStatus(string status)
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var parsedStatus = Enum.Parse<OrderStatus>(status);
            var count = dbContext.Orders.Count(o => o.Status == parsedStatus);
            
            yield return new Measurement<int>(count, new KeyValuePair<string, object?>("status", status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order count for status {Status}", status);
            yield return new Measurement<int>(0, new KeyValuePair<string, object?>("status", status));
        }
    }

    // Track orders by product category
    private void RecordOrderByCategory(string productName, int quantity, long value)
    {
        // Extract category from product name - simplified approach
        string category = GetCategoryFromProductName(productName);
        
        if (string.IsNullOrEmpty(category))
            category = "Uncategorized";
            
        lock (_lock)
        {
            if (!_ordersByCategory.TryGetValue(category, out var counter))
            {
                counter = _meter.CreateCounter<long>(
                    name: "business.order.by_category",
                    unit: "{orders}",
                    description: "Orders by product category");
                    
                _ordersByCategory[category] = counter;
            }
            
            counter.Add(quantity, new KeyValuePair<string, object?>("category", category),
                                 new KeyValuePair<string, object?>("value", value));
        }
    }

    // Helper to extract category
    private string GetCategoryFromProductName(string productName)
    {
        // Simplified - in real app would look up actual product category
        if (productName.Contains("Laptop") || productName.Contains("Smartphone"))
            return "Electronics";
        if (productName.Contains("Coffee"))
            return "Kitchen";
        return "Other";
    }

    // Get average processing time
    private IEnumerable<Measurement<double>> GetAverageProcessingTime()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            var shippedOrders = dbContext.Orders.Where(o => 
                o.Status == OrderStatus.Shipped && o.ShippedDate.HasValue);
            
            if (!shippedOrders.Any())
                yield return new Measurement<double>(0);
            else
            {
                var avgTime = shippedOrders.Average(o => 
                    (o.ShippedDate!.Value - o.OrderDate).TotalMilliseconds);
                yield return new Measurement<double>(avgTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating average processing time");
            yield return new Measurement<double>(0);
        }
    }

    // Get order conversion rate
    private IEnumerable<Measurement<double>> GetOrderConversionRate()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            var totalOrders = dbContext.Orders.Count();
            if (totalOrders == 0)
                yield return new Measurement<double>(0);
            else
            {
                var completedOrders = dbContext.Orders.Count(o => 
                    o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Shipped);
                var rate = (double)completedOrders / totalOrders * 100;
                yield return new Measurement<double>(rate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating order conversion rate");
            yield return new Measurement<double>(0);
        }
    }

    // Get orders awaiting fulfillment
    private IEnumerable<Measurement<int>> GetOrdersAwaitingFulfillment()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            var pendingCount = dbContext.Orders.Count(o => 
                o.Status == OrderStatus.Pending || o.Status == OrderStatus.Processing);
                
            yield return new Measurement<int>(pendingCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders awaiting fulfillment");
            yield return new Measurement<int>(0);
        }
    }
}
