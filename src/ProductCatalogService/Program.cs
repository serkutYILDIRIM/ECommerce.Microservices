using Microsoft.EntityFrameworkCore;
using ProductCatalogService.Data;
using ProductCatalogService.Models;
using ProductCatalogService.Telemetry;
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
builder.Services.AddEFCoreTracing<ProductDbContext>(
    TelemetryConfig.ServiceName,
    TelemetryConfig.ActivitySource,
    options => options.UseInMemoryDatabase("ProductCatalog"));

// Add HttpClientFactory for the trace test controller
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TelemetryConfig.ServiceName);
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Shared.Library.Controllers.TracingTestController).Assembly);

// Add HttpClientContextPropagator
builder.Services.AddSingleton(new Shared.Library.Telemetry.HttpClientContextPropagator(TelemetryConfig.ServiceName));

// Add OpenTelemetry with enhanced configuration, custom span processors, and samplers
builder.Services.AddServiceTelemetry(
    TelemetryConfig.ServiceName, 
    TelemetryConfig.ServiceVersion,
    configureSampling: options => {
        options.SamplerType = SamplerType.ParentBased;
        options.SamplingProbability = 0.25; // Higher sampling for product catalog
        options.UseCompositeSampling = true;
        options.MaxTracesPerSecond = 50; // Max 50 traces per second
        
        // Add a rule to always sample product searches
        options.Rules.Add(new SamplingRule
        {
            Name = "AlwaysSampleProductSearches",
            SpanNamePatterns = new List<string> { "*Product.Search*", "*Product.GetById*" },
            SamplingDecision = SamplingDecision.RecordAndSample
        });
    },
    tracerType: TracerProviderType.AspNet,
    customExporterConfigure: options => {
        options.SlowSpanThresholdMs = 500; // Lower threshold for product catalog service
        options.WithOperationNames("Product.Search", "Product.GetById");
    });

// Add services required for async context propagation
builder.Services.AddAsyncContextPropagation();

// Add services required for error handling
builder.Services.AddErrorHandling();

// Add health checks
builder.Services.AddHealthChecks();

// Add metrics services
builder.Services.AddPrometheusMetrics(TelemetryConfig.ServiceName, TelemetryConfig.ServiceVersion);
builder.Services.AddSingleton<ProductMetrics>();

// Add diagnostic context for logs
builder.Services.AddDiagnosticContext();
builder.Services.AddRequestContextLogger();

var app = builder.Build();

// Enable scope creation for metrics
// This allows static access to the service provider for observableGauges
static IServiceScope CreateScope() => 
    app.Services.CreateScope();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Initialize and seed the database
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
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
app.MapPrometheusScrapingEndpoint();

// Define API endpoints
app.MapGet("/products", async (ProductDbContext db, string? category = null, string? searchTerm = null, ProductMetrics metrics) =>
{
    // Use the custom ActivitySource for better control
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetAllProducts");
    activity?.SetTag("request.category_filter", category ?? "none");
    activity?.SetTag("request.search_term", searchTerm ?? "none");
    
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var query = db.Products.AsQueryable();
        
        // Apply category filter if provided
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category == category);
            activity?.AddEvent(new ActivityEvent("AppliedCategoryFilter", tags: new ActivityTagsCollection
            {
                { "category", category }
            }));
        }
        
        // Apply search term if provided
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(p => 
                p.Name.Contains(searchTerm) || 
                p.Description.Contains(searchTerm) ||
                p.Category.Contains(searchTerm));
            
            // Create a nested span specifically for search
            using var searchActivity = TelemetryConfig.ActivitySource.StartActivity("ProductSearch");
            searchActivity?.SetTag("search.term", searchTerm);
        }
        
        // Use tracked list to measure performance
        var products = await query.ToTrackedListAsync(
            "GetAllProducts", 
            TelemetryConfig.ActivitySource);
        stopwatch.Stop();
        
        // Record metrics
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            metrics.RecordSearchDuration(stopwatch.ElapsedMilliseconds, searchTerm, products.Count);
            activity?.RecordProductSearch(searchTerm, products.Count, stopwatch.ElapsedMilliseconds);
        }
        
        activity?.SetTag("product.count", products.Count);
        activity?.SetTag("query.execution_time_ms", stopwatch.ElapsedMilliseconds);
        
        // Log success for telemetry verification
        logger.LogInformation("Retrieved {Count} products in {ElapsedMs}ms", 
            products.Count, stopwatch.ElapsedMilliseconds);
        
        return Results.Ok(products);
    }
    catch (Exception ex)
    {
        // Record exception details in telemetry
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        logger.LogError(ex, "Error retrieving products");
        throw;
    }
})
.WithName("GetAllProducts")
.WithOpenApi();

app.MapGet("/products/{id}", async (int id, ProductDbContext db, ProductMetrics metrics) =>
{
    using var activity = TelemetryConfig.ActivitySource.StartActivity("GetProductById");
    activity?.SetTag("product.id", id);
    
    try
    {
        // Use tracked operations with detailed metrics for database access
        var query = db.Products.Where(p => p.Id == id);
        var product = await query.ToTrackedFirstOrDefaultAsync(
            "GetProductById", 
            TelemetryConfig.ActivitySource);
        
        if (product == null)
        {
            activity?.SetTag("product.found", false);
            logger.LogInformation("Product with ID {ProductId} not found", id);
            return Results.NotFound();
        }
        
        // Record product view metric
        metrics.RecordProductView(product);
        
        activity?.SetTag("product.found", true);
        activity?.SetTag("product.name", product.Name);
        
        logger.LogInformation("Retrieved product {ProductName} (ID: {ProductId})", product.Name, product.Id);
        return Results.Ok(product);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        logger.LogError(ex, "Error retrieving product with ID {ProductId}", id);
        throw;
    }
})
.WithName("GetProductById")
.WithOpenApi();

app.MapPost("/products", async (Product product, ProductDbContext db, ProductMetrics metrics) =>
{
    // Create custom span for product creation
    using var activity = TelemetryConfig.ActivitySource.StartActivity("CreateProduct");
    
    try
    {
        // Create a nested span specifically for validation
        using (var validationActivity = TelemetryConfig.ActivitySource.StartActivity("ValidateProduct"))
        {
            var validationErrors = new List<string>();
            
            // Validate product data
            if (string.IsNullOrWhiteSpace(product.Name))
                validationErrors.Add("Product name is required");
                
            if (product.Price <= 0)
                validationErrors.Add("Product price must be greater than zero");
                
            if (product.StockQuantity < 0)
                validationErrors.Add("Stock quantity cannot be negative");
                
            // Record validation results
            bool isValid = validationErrors.Count == 0;
            validationActivity?.RecordProductValidation(isValid, validationErrors);
            
            if (!isValid)
            {
                // If validation fails, return a bad request with validation errors
                activity?.SetStatus(ActivityStatusCode.Error, "Product validation failed");
                return Results.BadRequest(new { Errors = validationErrors });
            }
        }
        
        // Set creation metadata
        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;
        
        // Add nested span for database operation
        using (var dbActivity = TelemetryConfig.ActivitySource.StartActivity("SaveProductToDatabase"))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
            dbActivity?.SetTag("db.product.id", product.Id);
            dbActivity?.AddEvent(new ActivityEvent("ProductSavedToDatabase"));
        }
        
        // Record product creation metric
        metrics.RecordProductCreation(product);
        
        // Set tags for main span
        activity?.SetTag("product.id", product.Id);
        activity?.SetTag("product.name", product.Name);
        activity?.SetTag("product.category", product.Category);
        activity?.SetTag("product.price", product.Price);
        
        // Add business event for product creation
        activity?.AddEvent(new ActivityEvent("ProductCreated", tags: new ActivityTagsCollection
        {
            { "product.id", product.Id },
            { "product.name", product.Name },
            { "product.category", product.Category }
        }));
        
        logger.LogInformation("Created new product {ProductName} (ID: {ProductId})", product.Name, product.Id);
        return Results.Created($"/products/{product.Id}", product);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        logger.LogError(ex, "Error creating product");
        throw;
    }
})
.WithName("CreateProduct")
.WithOpenApi();

app.MapPut("/products/{id}", async (int id, Product updatedProduct, ProductDbContext db, ProductMetrics metrics) =>
{
    using var activity = new Activity("UpdateProduct");
    activity.Start();
    activity.SetTag("product.id", id);
    
    try
    {
        var product = await db.Products.FindAsync(id);
        
        if (product == null)
        {
            activity.SetTag("product.found", false);
            return Results.NotFound();
        }
        
        // Check if price has changed for price change metric
        if (product.Price != updatedProduct.Price)
        {
            metrics.RecordProductPriceChange(updatedProduct, product.Price);
        }
        
        // Update properties
        product.Name = updatedProduct.Name;
        product.Description = updatedProduct.Description;
        product.Price = updatedProduct.Price;
        product.StockQuantity = updatedProduct.StockQuantity;
        product.Category = updatedProduct.Category;
        product.UpdatedAt = DateTime.UtcNow;
        
        await db.SaveChangesAsync();
        
        // Record product update metric
        metrics.RecordProductUpdate(product);
        
        activity.SetTag("product.found", true);
        activity.SetTag("product.name", product.Name);
        
        return Results.Ok(product);
    }
    finally
    {
        activity.Stop();
    }
})
.WithName("UpdateProduct")
.WithOpenApi();

app.MapDelete("/products/{id}", async (int id, ProductDbContext db, ProductMetrics metrics) =>
{
    using var activity = new Activity("DeleteProduct");
    activity.Start();
    activity.SetTag("product.id", id);
    
    try
    {
        var product = await db.Products.FindAsync(id);
        
        if (product == null)
        {
            activity.SetTag("product.found", false);
            return Results.NotFound();
        }
        
        // Record product deletion metric before actually deleting
        metrics.RecordProductDeletion(product);
        
        db.Products.Remove(product);
        await db.SaveChangesAsync();
        
        activity.SetTag("product.found", true);
        
        return Results.NoContent();
    }
    finally
    {
        activity.Stop();
    }
})
.WithName("DeleteProduct")
.WithOpenApi();

app.Run();
