using Microsoft.EntityFrameworkCore;
using InventoryManagementService.Data;
using InventoryManagementService.Models;
using InventoryManagementService.Telemetry;
using Shared.Library.Telemetry;
using Shared.Library.Middleware;
using Shared.Library.Logging;
using Shared.Library.Controllers;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure structured logging with trace context
builder.AddStructuredLogging(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DbContext with enhanced tracing
builder.Services.AddEFCoreTracing<InventoryDbContext>(
    TelemetryConfig.ServiceName,
    TelemetryConfig.ActivitySource,
    options => options.UseInMemoryDatabase("InventoryManagement"));

// Add HttpClientFactory for the trace test controller
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TelemetryConfig.ServiceName);
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Shared.Library.Controllers.TracingTestController).Assembly);

// Add HttpClientContextPropagator
builder.Services.AddSingleton(new Shared.Library.Telemetry.HttpClientContextPropagator(TelemetryConfig.ServiceName));

// Add OpenTelemetry with enhanced configuration and custom span processors
builder.Services.AddServiceTelemetry(
    TelemetryConfig.ServiceName, 
    TelemetryConfig.ServiceVersion,
    configureSampling: options => {
        options.SamplerType = SamplerType.ParentBased;
        options.SamplingProbability = 0.3; // Medium sampling for inventory service
        options.UseCompositeSampling = true;
        options.MaxTracesPerSecond = 40; // Max 40 traces per second
        
        // Always sample stock level checks and reservations
        options.Rules.Add(new SamplingRule
        {
            Name = "InventoryOperations",
            SpanNamePatterns = new List<string> { 
                "*Inventory.CheckStock*", 
                "*Inventory.Reserve*",
                "*InventoryMonitoring*"
            },
            SamplingDecision = SamplingDecision.RecordAndSample
        });
        
        // Always sample operations for low stock items
        options.Rules.Add(new SamplingRule
        {
            Name = "LowStockItems",
            AttributeMatches = new List<AttributeMatch> 
            {
                new AttributeMatch { Key = "inventory.low_stock", Value = "true" }
            },
            SamplingDecision = SamplingDecision.RecordAndSample
        });
    },
    tracerType: TracerProviderType.AspNet,
    customExporterConfigure: options => {
        options.SlowSpanThresholdMs = 750; // Medium threshold for inventory service
        options.WithOperationNames("Inventory.CheckStock", "Inventory.Reserve", "Inventory.Release");
    });

// Add services required for async context propagation
builder.Services.AddAsyncContextPropagation();

// Add services required for error handling
builder.Services.AddErrorHandling();

// Add health checks
builder.Services.AddHealthChecks();

// Add metrics services
builder.Services.AddPrometheusMetrics(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);
builder.Services.AddSingleton<InventoryMetrics>();

// Add background service metrics
builder.Services.AddSingleton<BackgroundServiceMetrics>();

// Add diagnostic context for logs
builder.Services.AddDiagnosticContext();
builder.Services.AddRequestContextLogger();

// Add baggage management and business context enricher
builder.Services.AddBaggageManager();
builder.Services.AddBusinessContextEnricher();

// Add inventory business services
builder.Services.AddScoped<IInventoryBusinessRules, InventoryBusinessRules>();
builder.Services.AddSingleton<InventoryReservationQueue>();

// Register background services
builder.Services.AddHostedService<BackgroundServices.InventoryMonitoringService>();

var app = builder.Build();

// Enable scope creation for metrics
// This allows static access to the service provider for observableGauges
public static IServiceScope CreateScope() => 
    app.Services.CreateScope();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Initialize and seed the database
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseHttpsRedirection();

// Add performance monitoring middleware
app.UsePerformanceMonitoring();

// Add trace context middleware
app.UseTracing(TelemetryConfig.ServiceName);

// Add log enrichment middleware
app.UseLogEnrichment(TelemetryConfig.ServiceName);

// Configure middleware pipeline
// Exception handling should be early in the pipeline
app.UseErrorHandling();

// Configure middleware
app.UseActivityContextPropagation();
app.UseBaggagePropagation(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);

// Map controllers
app.MapControllers();

// Map Health endpoint
app.MapHealthChecks("/health");

// Expose Prometheus metrics
app.MapPrometheusScrapingEndpoint();

// Define API endpoints
app.MapGet("/inventory", async (InventoryDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetAllInventory");
    
    try
    {
        var query = db.Inventory.AsQueryable();
        var inventory = await query.ToTrackedListAsync(
            "GetAllInventory", 
            TelemetryConfig.ActivitySource);
            
        activity?.SetTag("inventory.count", inventory.Count);
        logger.LogInformation("Retrieved {Count} inventory items", inventory.Count);
        return Results.Ok(inventory);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error retrieving inventory items");
        throw;
    }
})
.WithName("GetAllInventory")
.WithOpenApi();

app.MapGet("/inventory/{id}", async (int id, InventoryDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetInventoryById");
    activity?.SetTag("inventory.id", id);
    
    try
    {
        var inventoryItem = await db.Inventory.FindAsync(id);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            logger.LogInformation("Inventory item with ID {InventoryId} not found", id);
            return Results.NotFound();
        }
        
        activity?.SetTag("inventory.found", true);
        activity?.SetTag("product.id", inventoryItem.ProductId);
        logger.LogInformation("Retrieved inventory item for product {ProductName} (ID: {ProductId})", 
            inventoryItem.ProductName, inventoryItem.ProductId);
        return Results.Ok(inventoryItem);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error retrieving inventory item with ID {InventoryId}", id);
        throw;
    }
})
.WithName("GetInventoryById")
.WithOpenApi();

app.MapGet("/inventory/product/{productId}", async (int productId, InventoryDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetInventoryByProductId");
    activity?.SetTag("product.id", productId);
    
    try
    {
        var inventoryItem = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == productId);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            logger.LogInformation("Inventory for product ID {ProductId} not found", productId);
            return Results.NotFound();
        }
        
        activity?.SetTag("inventory.found", true);
        activity?.SetTag("inventory.id", inventoryItem.Id);
        activity?.SetTag("inventory.quantity_available", inventoryItem.QuantityAvailable);
        logger.LogInformation("Retrieved inventory for product {ProductName} (ID: {ProductId}): {Quantity} available", 
            inventoryItem.ProductName, productId, inventoryItem.QuantityAvailable);
        return Results.Ok(inventoryItem);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error retrieving inventory for product ID {ProductId}", productId);
        throw;
    }
})
.WithName("GetInventoryByProductId")
.WithOpenApi();

app.MapGet("/inventory/check/{productId}", async (int productId, int quantity, InventoryDbContext db, InventoryMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("CheckInventoryAvailability");
    activity?.SetTag("product.id", productId);
    activity?.SetTag("quantity.requested", quantity);
    
    try
    {
        var inventoryItem = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == productId);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            activity?.AddEvent(new ActivityEvent("InventoryNotFound", tags: new ActivityTagsCollection
            {
                { "product.id", productId }
            }));
            
            // Record inventory check metric (not found)
            metrics.RecordInventoryCheck(productId, quantity, false);
            
            logger.LogInformation("Inventory for product ID {ProductId} not found during availability check", productId);
            return Results.NotFound();
        }
        
        var available = inventoryItem.QuantityAvailable - inventoryItem.QuantityReserved;
        var isAvailable = available >= quantity;
        
        // Record availability check in telemetry
        activity?.RecordAvailabilityCheck(productId, quantity, available, isAvailable);
        
        // Record inventory check metric
        metrics.RecordInventoryCheck(productId, quantity, isAvailable);
        
        logger.LogInformation("Checked inventory for product {ProductName} (ID: {ProductId}): {Available}/{Requested} available", 
            inventoryItem.ProductName, productId, available, quantity);
        
        if (!isAvailable)
        {
            logger.LogWarning("Insufficient inventory for product {ProductName} (ID: {ProductId}): {Available}/{Requested}", 
                inventoryItem.ProductName, productId, available, quantity);
                
            activity?.AddEvent(new ActivityEvent("InsufficientInventory", tags: new ActivityTagsCollection
            {
                { "product.name", inventoryItem.ProductName },
                { "quantity.requested", quantity },
                { "quantity.available", available },
                { "shortage", quantity - available }
            }));
            
            return Results.BadRequest($"Insufficient inventory for product {inventoryItem.ProductName}. Requested: {quantity}, Available: {available}");
        }
        
        activity?.AddEvent(new ActivityEvent("InventoryAvailable", tags: new ActivityTagsCollection
        {
            { "product.name", inventoryItem.ProductName },
            { "quantity.requested", quantity },
            { "quantity.available", available },
            { "quantity.remaining", available - quantity }
        }));
        
        return Results.Ok(new { Available = true, AvailableQuantity = available });
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error checking inventory for product ID {ProductId}", productId);
        throw;
    }
})
.WithName("CheckInventory")
.WithOpenApi();

app.MapPost("/inventory", async (InventoryItem inventoryItem, InventoryDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("CreateInventoryItem");
    activity?.SetTag("product.id", inventoryItem.ProductId);
    
    try
    {
        // Check if inventory for the product already exists
        var existingItem = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == inventoryItem.ProductId);
        
        if (existingItem != null)
        {
            activity?.SetTag("inventory.exists", true);
            logger.LogWarning("Attempted to create duplicate inventory for product ID {ProductId}", inventoryItem.ProductId);
            return Results.Conflict($"Inventory for product ID {inventoryItem.ProductId} already exists");
        }
        
        inventoryItem.LastUpdated = DateTime.UtcNow;
        
        db.Inventory.Add(inventoryItem);
        await db.SaveChangesAsync();
        
        activity?.SetTag("inventory.id", inventoryItem.Id);
        activity?.SetTag("inventory.quantity", inventoryItem.QuantityAvailable);
        
        logger.LogInformation("Created inventory for product {ProductName} (ID: {ProductId}) with {Quantity} units", 
            inventoryItem.ProductName, inventoryItem.ProductId, inventoryItem.QuantityAvailable);
        return Results.Created($"/inventory/{inventoryItem.Id}", inventoryItem);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        logger.LogError(ex, "Error creating inventory for product ID {ProductId}", inventoryItem.ProductId);
        throw;
    }
})
.WithName("CreateInventoryItem")
.WithOpenApi();

app.MapPut("/inventory/{id}", async (int id, InventoryItem updatedItem, InventoryDbContext db, InventoryMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("UpdateInventoryItem");
    activity?.SetTag("inventory.id", id);
    
    try
    {
        var inventoryItem = await db.Inventory.FindAsync(id);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            return Results.NotFound();
        }
        
        // Record if this is a manual adjustment (not from a sale)
        var quantityDelta = updatedItem.QuantityAvailable - inventoryItem.QuantityAvailable;
        if (quantityDelta != 0)
        {
            metrics.RecordInventoryAdjustment(
                inventoryItem.ProductId, 
                inventoryItem.ProductName, 
                quantityDelta, 
                "Manual inventory adjustment");
        }
        
        // Update properties
        inventoryItem.ProductName = updatedItem.ProductName;
        inventoryItem.QuantityAvailable = updatedItem.QuantityAvailable;
        inventoryItem.QuantityReserved = updatedItem.QuantityReserved;
        inventoryItem.ReorderThreshold = updatedItem.ReorderThreshold;
        inventoryItem.Location = updatedItem.Location;
        inventoryItem.LastUpdated = DateTime.UtcNow;
        
        if (updatedItem.QuantityAvailable > inventoryItem.QuantityAvailable)
        {
            inventoryItem.LastRestocked = DateTime.UtcNow;
        }
        
        await db.SaveChangesAsync();
        
        activity?.SetTag("inventory.found", true);
        activity?.SetTag("inventory.quantity", inventoryItem.QuantityAvailable);
        
        return Results.Ok(inventoryItem);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
})
.WithName("UpdateInventoryItem")
.WithOpenApi();

app.MapPost("/inventory/reserve", async (InventoryReservation reservation, InventoryDbContext db, InventoryMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("ReserveInventory");
    activity?.SetTag("product.id", reservation.ProductId);
    activity?.SetTag("quantity.requested", reservation.Quantity);
    
    var stopwatch = Stopwatch.StartNew();
    bool success = false;
    
    try
    {
        // Create a nested span for inventory lookup
        using var lookupActivity = TelemetryConfig.ActivitySource.StartActivity("FindInventoryForReservation");
        lookupActivity?.SetTag("product.id", reservation.ProductId);
        
        var inventoryItem = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == reservation.ProductId);
        
        if (inventoryItem == null)
        {
            lookupActivity?.SetTag("inventory.found", false);
            activity?.SetTag("inventory.found", false);
            
            activity?.AddEvent(new ActivityEvent("InventoryNotFound", tags: new ActivityTagsCollection
            {
                { "product.id", reservation.ProductId }
            }));
            
            // Record reservation metric (failed - not found)
            metrics.RecordInventoryReservation(reservation.ProductId, reservation.Quantity, false);
            
            return Results.NotFound();
        }
        
        lookupActivity?.SetTag("inventory.found", true);
        lookupActivity?.SetTag("product.name", inventoryItem.ProductName);
        
        // Create a nested span for availability calculation
        using var availabilityActivity = TelemetryConfig.ActivitySource.StartActivity("CheckAvailabilityForReservation");
        
        var availableForReservation = inventoryItem.QuantityAvailable - inventoryItem.QuantityReserved;
        
        availabilityActivity?.SetTag("inventory.quantity_available", inventoryItem.QuantityAvailable);
        availabilityActivity?.SetTag("inventory.quantity_reserved", inventoryItem.QuantityReserved);
        availabilityActivity?.SetTag("inventory.available_for_reservation", availableForReservation);
        availabilityActivity?.SetTag("quantity.requested", reservation.Quantity);
        availabilityActivity?.SetTag("inventory.sufficient", availableForReservation >= reservation.Quantity);
        
        if (availableForReservation < reservation.Quantity)
        {
            // Check if this is a stockout (zero available)
            if (availableForReservation == 0)
            {
                metrics.RecordStockout(inventoryItem, "Zero available inventory");
            }
            
            activity?.SetTag("inventory.sufficient", false);
            activity?.SetTag("inventory.available", availableForReservation);
            
            activity?.AddEvent(new ActivityEvent("InsufficientInventoryForReservation", tags: new ActivityTagsCollection
            {
                { "product.name", inventoryItem.ProductName },
                { "quantity.requested", reservation.Quantity },
                { "quantity.available", availableForReservation },
                { "shortage", reservation.Quantity - availableForReservation }
            }));
            
            // Record reservation metric (failed - insufficient)
            metrics.RecordInventoryReservation(reservation.ProductId, reservation.Quantity, false);
            
            return Results.BadRequest($"Cannot reserve {reservation.Quantity} units. Only {availableForReservation} available for product {inventoryItem.ProductName}");
        }
        
        // Create a nested span for updating the reservation
        using var updateActivity = TelemetryConfig.ActivitySource.StartActivity("UpdateInventoryReservation");
        updateActivity?.SetTag("product.id", inventoryItem.ProductId);
        updateActivity?.SetTag("product.name", inventoryItem.ProductName);
        
        // Record the quantity change for reserved items
        var oldReserved = inventoryItem.QuantityReserved;
        inventoryItem.QuantityReserved += reservation.Quantity;
        inventoryItem.LastUpdated = DateTime.UtcNow;
        
        updateActivity?.RecordQuantityChange("quantity_reserved", oldReserved, inventoryItem.QuantityReserved, "Customer reservation");
        
        // Record stock level change metrics
        metrics.RecordStockLevelChange(inventoryItem.ProductId, oldReserved, inventoryItem.QuantityReserved, "Reservation");
        
        // Create a nested span for database update
        using var dbActivity = TelemetryConfig.ActivitySource.StartActivity("CommitReservationToDatabase");
        await db.SaveChangesAsync();
        dbActivity?.AddEvent(new ActivityEvent("ReservationCommitted"));
        
        stopwatch.Stop();
        
        var remainingAvailable = inventoryItem.QuantityAvailable - inventoryItem.QuantityReserved;
        
        // Check for low stock after reservation
        if (remainingAvailable <= inventoryItem.ReorderThreshold)
        {
            metrics.RecordLowStockEvent(inventoryItem);
        }
        
        // Record estimated inventory value (simplified, would use actual pricing in real app)
        double estimatedItemValue = 100.0; // Placeholder for demo
        double totalValue = estimatedItemValue * inventoryItem.QuantityAvailable;
        metrics.RecordInventoryValue(inventoryItem.ProductId, inventoryItem.ProductName, estimatedItemValue, totalValue);
        
        // Calculate and record simplified turnover rate (would be more sophisticated in real app)
        double turnoverRate = inventoryItem.QuantityReserved > 0 ? 
            (double)inventoryItem.QuantityReserved / inventoryItem.QuantityAvailable : 0;
        metrics.RecordStockTurnoverRate(inventoryItem.ProductId, inventoryItem.ProductName, turnoverRate);
        
        // Record reservation success
        success = true;
        metrics.RecordInventoryReservation(reservation.ProductId, reservation.Quantity, true);
        
        // Set final tags on the parent span
        activity?.SetTag("inventory.found", true);
        activity?.SetTag("inventory.sufficient", true);
        activity?.SetTag("inventory.new_reserved", inventoryItem.QuantityReserved);
        activity?.SetTag("inventory.remaining_available", remainingAvailable);
        activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        
        // Add a summary event
        activity?.AddEvent(new ActivityEvent("InventoryReservationCompleted", tags: new ActivityTagsCollection
        {
            { "product.name", inventoryItem.ProductName },
            { "quantity.reserved", reservation.Quantity },
            { "quantity.remaining_available", remainingAvailable },
            { "duration_ms", stopwatch.ElapsedMilliseconds }
        }));
        
        logger.LogInformation("Reserved {Quantity} units of {ProductName} (ID: {ProductId}). Remaining available: {Remaining}", 
            reservation.Quantity, inventoryItem.ProductName, inventoryItem.ProductId, remainingAvailable);
        
        return Results.Ok(new { 
            Reserved = true, 
            ProductId = reservation.ProductId,
            Quantity = reservation.Quantity,
            RemainingAvailable = remainingAvailable
        });
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        logger.LogError(ex, "Error reserving inventory for product ID {ProductId}", reservation.ProductId);
        throw;
    }
    finally
    {
        stopwatch.Stop();
        // Record reservation processing time
        metrics.RecordReservationProcessingTime(stopwatch.ElapsedMilliseconds, reservation.ProductId, reservation.Quantity, success);
    }
})
.WithName("ReserveInventory")
.WithOpenApi();

app.MapPost("/inventory/fulfill", async (InventoryReservation fulfillment, InventoryDbContext db, InventoryMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("FulfillInventory");
    activity?.SetTag("product.id", fulfillment.ProductId);
    activity?.SetTag("quantity", fulfillment.Quantity);
    
    try
    {
        var inventoryItem = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == fulfillment.ProductId);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            
            // Record fulfillment metric (failed - not found)
            metrics.RecordInventoryFulfillment(fulfillment.ProductId, fulfillment.Quantity, false);
            
            return Results.NotFound();
        }
        
        // Ensure we have enough reserved and available inventory
        if (inventoryItem.QuantityReserved < fulfillment.Quantity || inventoryItem.QuantityAvailable < fulfillment.Quantity)
        {
            activity?.SetTag("inventory.sufficient", false);
            
            // Record fulfillment metric (failed - insufficient)
            metrics.RecordInventoryFulfillment(fulfillment.ProductId, fulfillment.Quantity, false);
            
            return Results.BadRequest("Insufficient inventory or reservation for fulfillment");
        }
        
        // Update inventory - record old values for metrics
        var oldReserved = inventoryItem.QuantityReserved;
        var oldAvailable = inventoryItem.QuantityAvailable;
        
        inventoryItem.QuantityReserved -= fulfillment.Quantity;
        inventoryItem.QuantityAvailable -= fulfillment.Quantity;
        inventoryItem.LastUpdated = DateTime.UtcNow;
        
        // Record stock level changes for metrics
        metrics.RecordStockLevelChange(inventoryItem.ProductId, oldReserved, inventoryItem.QuantityReserved, "Fulfillment-Reserved");
        metrics.RecordStockLevelChange(inventoryItem.ProductId, oldAvailable, inventoryItem.QuantityAvailable, "Fulfillment-Available");
        
        // Check if we need to reorder
        bool needsReorder = inventoryItem.QuantityAvailable <= inventoryItem.ReorderThreshold;
        if (needsReorder)
        {
            metrics.RecordLowStockEvent(inventoryItem);
        }
        
        await db.SaveChangesAsync();
        
        // Record fulfillment success metric
        metrics.RecordInventoryFulfillment(fulfillment.ProductId, fulfillment.Quantity, true);
        
        activity?.SetTag("inventory.found", true);
        activity?.SetTag("inventory.sufficient", true);
        activity?.SetTag("inventory.remaining", inventoryItem.QuantityAvailable);
        activity?.SetTag("inventory.needs_reorder", needsReorder);
        
        return Results.Ok(new { 
            Fulfilled = true, 
            ProductId = fulfillment.ProductId,
            Quantity = fulfillment.Quantity,
            RemainingQuantity = inventoryItem.QuantityAvailable,
            NeedsReorder = needsReorder
        });
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        // Record fulfillment failure metric
        metrics.RecordInventoryFulfillment(fulfillment.ProductId, fulfillment.Quantity, false);
        
        throw;
    }
})
.WithName("FulfillInventory")
.WithOpenApi();

app.MapDelete("/inventory/{id}", async (int id, InventoryDbContext db) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("DeleteInventoryItem");
    activity?.SetTag("inventory.id", id);
    
    try
    {
        var inventoryItem = await db.Inventory.FindAsync(id);
        
        if (inventoryItem == null)
        {
            activity?.SetTag("inventory.found", false);
            return Results.NotFound();
        }
        
        activity?.SetTag("product.id", inventoryItem.ProductId);
        
        db.Inventory.Remove(inventoryItem);
        await db.SaveChangesAsync();
        
        activity?.SetTag("inventory.found", true);
        
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
})
.WithName("DeleteInventoryItem")
.WithOpenApi();

app.Run();
