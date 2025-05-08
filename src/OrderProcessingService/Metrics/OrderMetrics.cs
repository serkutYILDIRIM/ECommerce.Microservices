using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.DependencyInjection; // Added for IServiceProvider
using Microsoft.Extensions.Hosting; // Added for IHostedService
using Microsoft.Extensions.Logging; // Added for ILogger
using OrderProcessingService.Data; // Added for OrderDbContext
using OrderProcessingService.Models; // Added for OrderStatus

namespace OrderProcessingService.Metrics
{
    public class OrderMetrics : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderMetrics> _logger;
        private readonly Meter _meter;
        private ObservableGauge<int>? _pendingOrdersGauge;
        private ObservableGauge<int>? _processingOrdersGauge;
        private ObservableGauge<int>? _completedOrdersGauge;
        private Timer? _timer;

        public OrderMetrics(IServiceProvider serviceProvider, ILogger<OrderMetrics> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _meter = new Meter("OrderProcessingService.Metrics", "1.0.0");
            InitializeMetrics();
        }

        private void InitializeMetrics()
        {
            _pendingOrdersGauge = _meter.CreateObservableGauge<int>(
                "orders.pending.count",
                ObservePendingOrders, // Changed to method group
                description: "Number of orders currently in Pending status.");

            _processingOrdersGauge = _meter.CreateObservableGauge<int>(
                "orders.processing.count",
                ObserveProcessingOrders, // Changed to method group
                description: "Number of orders currently in Processing status.");

            _completedOrdersGauge = _meter.CreateObservableGauge<int>(
                "orders.completed.count",
                ObserveCompletedOrders, // Changed to method group
                description: "Number of orders currently in Completed status.");
        }

        // Method to observe Pending orders
        private int ObservePendingOrders()
        {
            return GetOrderStatusCount(OrderStatus.Pending);
        }

        // Method to observe Processing orders
        private int ObserveProcessingOrders()
        {
            return GetOrderStatusCount(OrderStatus.Processing);
        }

        // Method to observe Completed orders
        private int ObserveCompletedOrders()
        {
            // Assuming Completed, Shipped, Delivered are terminal states
            return GetOrderStatusCount(OrderStatus.Completed) +
                   GetOrderStatusCount(OrderStatus.Shipped) +
                   GetOrderStatusCount(OrderStatus.Delivered);
        }

        // Helper method to get count for a specific status
        private int GetOrderStatusCount(OrderStatus status)
        {
            // Create a scope to resolve DbContext
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            try
            {
                return dbContext.Orders.Count(o => o.Status == status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order count for status {Status}", status);
                return 0; // Return 0 if there's an error
            }
        }

        // Removed the problematic GetOrderMeasurements method which used yield return incorrectly
        // and returned IEnumerable<Measurement<int>> instead of int.

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OrderMetrics Hosted Service running.");
            // Timer is not strictly necessary for Observable Gauges as they are polled,
            // but kept if other periodic tasks were intended. Removed for simplicity now.
            // _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        // Removed DoWork method as it's not needed for observable gauges

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OrderMetrics Hosted Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _meter.Dispose();
            GC.SuppressFinalize(this); // Added standard Dispose pattern
        }

        public void RecordOrderCreation(Order order)
        {
            // Logic to record order creation metrics
            _logger.LogInformation("Order created: {OrderId}", order.Id);
        }

        public void RecordOrderProcessingDuration(Order order, TimeSpan duration)
        {
            // Logic to record order processing duration metrics
            _logger.LogInformation("Order {OrderId} processed in {Duration}ms", order.Id, duration.TotalMilliseconds);
        }

        public void RecordOrderStatusChange(int orderId, OrderStatus oldStatus, OrderStatus newStatus)
        {
            // Logic to record order status change metrics
            _logger.LogInformation("Order {OrderId} status changed from {OldStatus} to {NewStatus}", orderId, oldStatus, newStatus);
        }
    }
}
