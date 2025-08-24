using Microsoft.EntityFrameworkCore;
using OrderProcessingService.Data;
using OrderProcessingService.Models;
using OrderProcessingService.Services;
using OrderProcessingService.Telemetry;
using Shared.Library.Telemetry;
using Shared.Library.Middleware;
using Shared.Library.Logging;
using Shared.Library.Controllers;
using System.Diagnostics;
using Shared.Library.Data;
using Shared.Library.Telemetry.Sampling;
using OpenTelemetry.Trace;
using Shared.Library.DependencyInjection;
using Shared.Library.Metrics;
using OrderProcessingService.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Configure structured logging with trace context
builder.AddStructuredLogging(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DbContext with enhanced tracing
builder.Services.AddEFCoreTracing<OrderDbContext>(
    TelemetryConfig.ServiceName,
    TelemetryConfig.ActivitySource,
    options => options.UseInMemoryDatabase("OrderProcessing"));

// Add HttpClientFactory for the trace test controller
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TelemetryConfig.ServiceName);
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Shared.Library.Controllers.TracingTestController).Assembly);

// Add HttpClientContextPropagator
builder.Services.AddSingleton(new Shared.Library.Telemetry.HttpClientContextPropagator(TelemetryConfig.ServiceName));

// Configure HTTP clients
builder.Services.AddHttpClient<ProductCatalogService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ServiceUrls:ProductCatalogService"] ?? "https://localhost:7001");
});

builder.Services.AddHttpClient<InventoryService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ServiceUrls:InventoryManagementService"] ?? "https://localhost:7002");
});

// Add OpenTelemetry with enhanced configuration, custom span processors, and samplers
IServiceCollection serviceCollection = builder.Services.AddOpenTelemetryServices(
    configuration: builder.Configuration,
    serviceName: TelemetryConfig.ServiceName,
    serviceVersion: TelemetryConfig.ServiceVersion,
    configure: options =>
    {
        options.SamplerType = SamplerType.ParentBased;
        options.SamplingProbability = 0.5; // Higher sampling for order processing
        options.UseCompositeSampling = true;
        options.MaxTracesPerSecond = 30; // Max 30 traces per second

        // Add a rule to always sample order creation and processing
        options.Rules.Add(new SamplingRule
        {
            Name = "CriticalOrderOperations",
            SpanNamePatterns = new List<string> { "*Order.Process*", "*Order.Create*", "*Payment*" },
            SamplingDecision = SamplingDecision.RecordAndSample
        });

        // Always sample operations from specific clients
        options.Rules.Add(new SamplingRule
        {
            Name = "PriorityClients",
            AttributeMatches = new List<AttributeMatch>
            {
                new AttributeMatch { Key = "client.tier", Value = "premium" }
            },
            SamplingDecision = SamplingDecision.RecordAndSample
        });
    });

// Add services required for async context propagation
builder.Services.AddAsyncContextPropagation();

// Add services required for error handling
builder.Services.AddErrorHandling();

// Add health checks
builder.Services.AddHealthChecks();

// Add metrics services
builder.Services.AddPrometheusMetrics(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);
builder.Services.AddSingleton<OrderMetrics>();

// Add diagnostic context for logs
builder.Services.AddDiagnosticContext();
builder.Services.AddRequestContextLogger();

var app = builder.Build();

// Create logger instance
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Program");

// Enable scope creation for metrics
// This allows static access to the service provider for observableGauges
IServiceScope CreateScope() =>
  app.Services.CreateScope();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Initialize and seed the database
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseHttpsRedirection();

// Configure middleware pipeline
// Exception handling should be early in the pipeline
app.UseErrorHandling();

// Add performance monitoring middleware
app.UsePerformanceMonitoring();

// Add trace context middleware
app.UseTracing(TelemetryConfig.ServiceName);

// Add log enrichment middleware
app.UseLogEnrichment(TelemetryConfig.ServiceName);

// Configure middleware
app.UseActivityContextPropagation();

// Map controllers
app.MapControllers();

// Map Health endpoint
app.MapHealthChecks("/health");

// Expose Prometheus metrics
app.UseOpenTelemetryPrometheusScrapingEndpoint();

// Define API endpoints
app.MapGet("/orders", async (OrderDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetAllOrders");

    try
    {
        var query = db.Orders.Include(o => o.Items);
        var orders = await query.ToTrackedListAsync(
            "GetAllOrders",
            TelemetryConfig.ActivitySource);

        activity?.SetTag("order.count", orders.Count);
        logger.LogInformation("Retrieved {Count} orders", orders.Count);
        return Results.Ok(orders);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error retrieving orders");
        throw;
    }
})
.WithName("GetAllOrders")
.WithOpenApi();

app.MapGet("/orders/{id}", async (int id, OrderDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetOrderById");
    activity?.SetTag("order.id", id);

    try
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            activity?.SetTag("order.found", false);
            logger.LogInformation("Order with ID {OrderId} not found", id);
            return Results.NotFound();
        }

        activity?.SetTag("order.found", true);
        activity?.SetTag("order.status", order.Status.ToString());
        activity?.SetTag("order.items_count", order.Items.Count);

        logger.LogInformation("Retrieved order {OrderId} with {ItemCount} items", id, order.Items.Count);
        return Results.Ok(order);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error retrieving order with ID {OrderId}", id);
        throw;
    }
})
.WithName("GetOrderById")
.WithOpenApi();

app.MapPost("/orders", async (
    OrderDbContext db,
    ProductCatalogService productService,
    InventoryService inventoryService,
    Order order,
    OrderMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("ProcessOrderWorkflow");
    var stopwatch = Stopwatch.StartNew();
    bool success = false;

    try
    {
        activity?.SetTag("customer.name", order.CustomerName);
        activity?.SetTag("customer.email", order.CustomerEmail);
        activity?.SetTag("order.items_count", order.Items.Count);

        // Record the start of order processing workflow
        activity?.AddEvent(new ActivityEvent("OrderWorkflowStarted", tags: new ActivityTagsCollection
        {
            { "customer.name", order.CustomerName },
            { "items.count", order.Items.Count }
        }));

        // Step 1: Validate customer information
        using (var customerValidationActivity = TelemetryConfig.ActivitySource.StartActivity("ValidateCustomerInformation"))
        {
            bool isValid = true;
            var validationErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(order.CustomerName))
            {
                isValid = false;
                validationErrors.Add("Customer name is required");
            }

            if (string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                isValid = false;
                validationErrors.Add("Customer email is required");
            }
            else if (!order.CustomerEmail.Contains('@'))
            {
                isValid = false;
                validationErrors.Add("Invalid email format");
            }

            customerValidationActivity?.SetTag("validation.success", isValid);
            if (!isValid)
            {
                for (int i = 0; i < validationErrors.Count; i++)
                {
                    customerValidationActivity?.SetTag($"validation.error.{i}", validationErrors[i]);
                }

                activity?.RecordOrderWorkflowStep("CustomerValidation", false, string.Join(", ", validationErrors));
                return Results.BadRequest(new { Errors = validationErrors });
            }

            activity?.RecordOrderWorkflowStep("CustomerValidation", true);
        }

        // Step 2: Check if products exist and have sufficient stock
        foreach (var item in order.Items)
        {
            // Create a nested activity for checking each product
            using var productCheckActivity = TelemetryConfig.ActivitySource.StartActivity("CheckProductAvailability");
            productCheckActivity?.SetTag("product.id", item.ProductId);
            productCheckActivity?.SetTag("quantity.requested", item.Quantity);

            // Check product existence
            activity?.RecordOrderWorkflowStep("ProductLookup", true, $"Looking up product {item.ProductId}");
            var product = await productService.GetProductAsync(item.ProductId);
            if (product == null)
            {
                productCheckActivity?.SetTag("product.found", false);
                activity?.RecordOrderWorkflowStep("ProductLookup", false, $"Product not found: {item.ProductId}");
                return Results.BadRequest($"Product with ID {item.ProductId} not found");
            }

            productCheckActivity?.SetTag("product.found", true);
            productCheckActivity?.SetTag("product.name", product.Name);

            // Check inventory availability
            activity?.RecordOrderWorkflowStep("InventoryCheck", true, $"Checking inventory for product {product.Name}");
            var inventoryResult = await inventoryService.CheckAndReserveInventoryAsync(
                order.Id,
                item.ProductId,
                item.Quantity);
            if (!inventoryResult.Success)
            {
                productCheckActivity?.SetTag("inventory.available", false);
                activity?.RecordOrderWorkflowStep("InventoryCheck", false, $"Insufficient stock for product {product.Name}");
                return Results.BadRequest($"Insufficient stock for product {product.Name}");
            }

            productCheckActivity?.SetTag("inventory.available", true);


            // Set product details from lookup
            item.ProductName = product.Name;
            item.UnitPrice = product.Price;

            productCheckActivity?.AddEvent(new ActivityEvent("ProductCheckCompleted", tags: new ActivityTagsCollection
            {
                { "product.name", product.Name },
                { "product.price", product.Price },
                { "quantity", item.Quantity },
                { "subtotal", item.UnitPrice * item.Quantity }
            }));
        }

        activity?.RecordOrderWorkflowStep("ProductsValidation", true, $"All {order.Items.Count} products validated");

        // Step 3: Finalize the order
        order.TotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.OrderDate = DateTime.UtcNow;
        order.Status = OrderStatus.Pending;

        // Record the order finalization
        activity?.AddEvent(new ActivityEvent("OrderFinalized", tags: new ActivityTagsCollection
        {
            { "order.total", order.TotalAmount },
            { "order.date", order.OrderDate.ToString("o") }
        }));

        // Step 4: Save the order
        using (var dbActivity = TelemetryConfig.ActivitySource.StartActivity("SaveOrderToDatabase"))
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            dbActivity?.SetTag("order.id", order.Id);
            dbActivity?.AddEvent(new ActivityEvent("OrderSavedToDatabase"));
        }

        activity?.RecordOrderWorkflowStep("DatabaseSave", true, $"Order saved with ID {order.Id}");

        // Step 5: Reserve inventory for each item
        using (var reservationActivity = TelemetryConfig.ActivitySource.StartActivity("ReserveInventory"))
        {
            reservationActivity?.SetTag("order.id", order.Id);
            reservationActivity?.SetTag("items.count", order.Items.Count);

            int reservedItems = 0;
            foreach (var item in order.Items)
            {
                bool reserved = await inventoryService.CheckAndReserveInventoryAsync(
                order.Id, item.ProductId, item.Quantity).ContinueWith(t => t.Result.Success);

                if (reserved) reservedItems++;

                reservationActivity?.AddEvent(new ActivityEvent(reserved ? "ItemReserved" : "ItemReservationFailed",
                    tags: new ActivityTagsCollection
                    {
                        { "product.id", item.ProductId },
                        { "product.name", item.ProductName },
                        { "quantity", item.Quantity },
                        { "success", reserved }
                    }));
            }

            reservationActivity?.SetTag("reservation.success_count", reservedItems);
            reservationActivity?.SetTag("reservation.total_count", order.Items.Count);
            reservationActivity?.SetTag("reservation.all_succeeded", reservedItems == order.Items.Count);
        }

        activity?.RecordOrderWorkflowStep("InventoryReservation", true, $"Reserved inventory for {order.Items.Count} items");

        // Update the main activity with the final order information
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("order.total", order.TotalAmount);
        activity?.SetTag("order.status", order.Status.ToString());

        // Record the completion of the workflow
        activity?.AddEvent(new ActivityEvent("OrderWorkflowCompleted", tags: new ActivityTagsCollection
        {
            { "order.id", order.Id },
            { "order.total", order.TotalAmount },
            { "order.items", order.Items.Count }
        }));

        // Record order creation metric
        success = true;
        metrics.RecordOrderCreation(order);

        logger.LogInformation("Created order {OrderId} with total {Total:C}", order.Id, order.TotalAmount);
        return Results.Created($"/orders/{order.Id}", order);
    }
    catch (Exception ex)
    {
        activity?.RecordOrderWorkflowStep("WorkflowFailed", false, ex.Message);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error creating order");
        return Results.Problem(ex.Message);
    }
    finally
    {
        stopwatch.Stop();
        // Record processing duration
        if (order.Id > 0) // Only record if the order was created in the database
        {
            metrics.RecordOrderProcessingDuration(order, TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds));
        }
    }
})
.WithName("CreateOrder")
.WithOpenApi();

app.MapPut("/orders/{id}/status", async (int id, OrderStatus newStatus, OrderDbContext db, OrderMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("UpdateOrderStatus");
    activity?.SetTag("order.id", id);
    activity?.SetTag("order.status.new", newStatus.ToString());

    try
    {
        var order = await db.Orders.FindAsync(id);

        if (order == null)
        {
            activity?.SetTag("order.found", false);
            logger.LogInformation("Order with ID {OrderId} not found", id);
            return Results.NotFound();
        }

        // Record the status change
        var oldStatus = order.Status;
        activity?.RecordOrderStatusChange(oldStatus, newStatus);

        // Validate the status transition
        using (var validationActivity = TelemetryConfig.ActivitySource.StartActivity("ValidateStatusTransition"))
        {
            bool isValidTransition = IsValidStatusTransition(oldStatus, newStatus);
            validationActivity?.SetTag("status.transition.from", oldStatus.ToString());
            validationActivity?.SetTag("status.transition.to", newStatus.ToString());
            validationActivity?.SetTag("status.transition.valid", isValidTransition);

            if (!isValidTransition)
            {
                validationActivity?.SetStatus(ActivityStatusCode.Error, $"Invalid status transition: {oldStatus} -> {newStatus}");
                return Results.BadRequest($"Invalid status transition from {oldStatus} to {newStatus}");
            }
        }

        // Update the status
        order.Status = newStatus;

        // If order is shipped, set the shipped date
        if (newStatus == OrderStatus.Shipped)
        {
            order.ShippedDate = DateTime.UtcNow;
            activity?.SetTag("order.shipped_date", order.ShippedDate.ToString());
        }

        // Record the status update in the database
        using (var dbActivity = TelemetryConfig.ActivitySource.StartActivity("SaveStatusChange"))
        {
            await db.SaveChangesAsync();
            dbActivity?.AddEvent(new ActivityEvent("StatusChangeCommitted"));
        }

        activity?.SetTag("order.found", true);
        activity?.AddEvent(new ActivityEvent("OrderStatusUpdated", tags: new ActivityTagsCollection
        {
            { "order.id", id },
            { "status.old", oldStatus.ToString() },
            { "status.new", newStatus.ToString() }
        }));

        // Record order status change metric
        metrics.RecordOrderStatusChange(id, oldStatus, newStatus);

        logger.LogInformation("Updated order {OrderId} status from {OldStatus} to {NewStatus}",
            id, oldStatus, newStatus);
        return Results.Ok(order);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error updating status for order {OrderId}", id);
        throw;
    }
})
.WithName("UpdateOrderStatus")
.WithOpenApi();

// Helper method for status transition validation
bool IsValidStatusTransition(OrderStatus current, OrderStatus next)
{
    return (current, next) switch
    {
        (OrderStatus.Pending, OrderStatus.Processing) => true,
        (OrderStatus.Pending, OrderStatus.Cancelled) => true,
        (OrderStatus.Processing, OrderStatus.Shipped) => true,
        (OrderStatus.Processing, OrderStatus.Cancelled) => true,
        (OrderStatus.Shipped, OrderStatus.Delivered) => true,
        _ => false
    };
}

app.MapDelete("/orders/{id}", async (int id, OrderDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("DeleteOrder");
    activity?.SetTag("order.id", id);

    try
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            activity?.SetTag("order.found", false);
            logger.LogInformation("Order with ID {OrderId} not found for deletion", id);
            return Results.NotFound();
        }

        activity?.SetTag("order.items_count", order.Items.Count);

        // Remove order items first
        db.OrderItems.RemoveRange(order.Items);

        // Then remove the order
        db.Orders.Remove(order);
        await db.SaveChangesAsync();

        activity?.SetTag("order.found", true);

        logger.LogInformation("Deleted order {OrderId} with {ItemCount} items", id, order.Items.Count);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error deleting order {OrderId}", id);
        throw;
    }
})
.WithName("DeleteOrder")
.WithOpenApi();

app.Run();
