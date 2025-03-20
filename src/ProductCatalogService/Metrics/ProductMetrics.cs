using System.Diagnostics.Metrics;
using ProductCatalogService.Models;
using Shared.Library.Metrics;

namespace ProductCatalogService.Metrics;

/// <summary>
/// Provides metrics for the Product Catalog Service
/// </summary>
public class ProductMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _productViewsCounter;
    private readonly Counter<long> _productCreationsCounter;
    private readonly Counter<long> _productUpdatesCounter;
    private readonly Counter<long> _productDeletionsCounter;
    private readonly Histogram<double> _searchDurationHistogram;
    private readonly Dictionary<string, ObservableGauge<int>> _categoryProductCountGauges = new();
    private readonly ILogger<ProductMetrics> _logger;
    private readonly object _lock = new();
    
    // Business metrics
    private readonly Counter<long> _productViewsByCategory;
    private readonly Histogram<double> _productPriceDistribution;
    private readonly Dictionary<string, UpDownCounter<long>> _activeProductsCounters = new();
    private readonly Dictionary<string, ObservableGauge<double>> _categoryPriceGauges = new();

    public ProductMetrics(MeterProvider meterProvider, ILogger<ProductMetrics> logger)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;

        // Create counters
        _productViewsCounter = _meter.CreateCounter<long>(
            name: "product.views",
            unit: "{views}",
            description: "Number of times products have been viewed");

        _productCreationsCounter = _meter.CreateCounter<long>(
            name: "product.creations",
            unit: "{products}",
            description: "Number of products created");

        _productUpdatesCounter = _meter.CreateCounter<long>(
            name: "product.updates",
            unit: "{products}",
            description: "Number of products updated");

        _productDeletionsCounter = _meter.CreateCounter<long>(
            name: "product.deletions",
            unit: "{products}",
            description: "Number of products deleted");

        // Create a histogram for search duration tracking
        _searchDurationHistogram = _meter.CreateHistogram<double>(
            name: "product.search.duration",
            unit: "ms",
            description: "Duration of product search operations");

        // Create an observable gauge for total product count
        _meter.CreateObservableGauge(
            name: "product.count.total",
            observeValue: GetTotalProductCount,
            unit: "{products}",
            description: "Total number of products in the catalog");

        // Business metrics
        _productViewsByCategory = _meter.CreateCounter<long>(
            name: "business.product.views_by_category",
            unit: "{views}",
            description: "Number of product views by category");

        _productPriceDistribution = _meter.CreateHistogram<double>(
            name: "business.product.price_distribution",
            unit: "{currency}",
            description: "Distribution of product prices");

        // Create an observable gauge for product catalog health
        _meter.CreateObservableGauge(
            name: "business.product.catalog_health",
            observeValue: GetProductCatalogHealth,
            unit: "{percent}",
            description: "Health of product catalog (percentage of products with stock)");
            
        // Create an observable gauge for most viewed category
        _meter.CreateObservableGauge(
            name: "business.product.popular_categories",
            observeValue: GetPopularCategories,
            unit: "{views}",
            description: "Most viewed product categories");
            
        // Create an observable gauge for price volatility
        _meter.CreateObservableGauge(
            name: "business.product.price_volatility",
            observeValue: GetPriceVolatility,
            unit: "{percent}",
            description: "Price change frequency as percentage");

        _logger.LogInformation("Product metrics initialized");
    }

    public void RecordProductView(Product product)
    {
        _productViewsCounter.Add(1, new KeyValuePair<string, object?>("product.id", product.Id),
                                   new KeyValuePair<string, object?>("product.name", product.Name),
                                   new KeyValuePair<string, object?>("product.category", product.Category));
        
        // Record business metrics for product views by category
        _productViewsByCategory.Add(1, 
            new KeyValuePair<string, object?>("product.id", product.Id),
            new KeyValuePair<string, object?>("product.name", product.Name),
            new KeyValuePair<string, object?>("product.category", product.Category));
            
        // Record price in histogram for price analysis
        _productPriceDistribution.Record(Convert.ToDouble(product.Price), 
            new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordProductCreation(Product product)
    {
        _productCreationsCounter.Add(1, new KeyValuePair<string, object?>("product.category", product.Category));
        EnsureCategoryGaugeExists(product.Category);
        
        // Update active product counters
        UpdateActiveProductCounter(product.Category, 1);
        
        // Ensure we have a price gauge for this category
        EnsureCategoryPriceGaugeExists(product.Category);
    }

    public void RecordProductUpdate(Product product)
    {
        _productUpdatesCounter.Add(1, new KeyValuePair<string, object?>("product.id", product.Id),
                                     new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordProductDeletion(Product product)
    {
        _productDeletionsCounter.Add(1, new KeyValuePair<string, object?>("product.category", product.Category));
        
        // Update active product counters
        UpdateActiveProductCounter(product.Category, -1);
    }

    public void RecordSearchDuration(double durationMs, string searchTerm, int resultCount)
    {
        _searchDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("search.term", searchTerm),
            new KeyValuePair<string, object?>("search.result_count", resultCount));
    }

    // Added method to track category price changes
    public void RecordProductPriceChange(Product product, decimal oldPrice)
    {
        double priceChangePercent = oldPrice == 0 ? 0 : 
            Math.Abs(Convert.ToDouble((product.Price - oldPrice) / oldPrice) * 100);
            
        _meter.CreateHistogram<double>(
            name: "business.product.price_changes",
            unit: "{percent}",
            description: "Percentage changes in product prices")
            .Record(priceChangePercent, 
                new KeyValuePair<string, object?>("product.id", product.Id),
                new KeyValuePair<string, object?>("product.name", product.Name),
                new KeyValuePair<string, object?>("product.category", product.Category),
                new KeyValuePair<string, object?>("price.old", Convert.ToDouble(oldPrice)),
                new KeyValuePair<string, object?>("price.new", Convert.ToDouble(product.Price)));
    }

    // Observable gauge functions must return an IEnumerable<Measurement<T>>
    private IEnumerable<Measurement<int>> GetTotalProductCount()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var count = dbContext.Products.Count();
            
            yield return new Measurement<int>(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total product count for metrics");
            yield return new Measurement<int>(0);
        }
    }

    private void EnsureCategoryGaugeExists(string category)
    {
        if (string.IsNullOrEmpty(category)) return;
        
        lock (_lock)
        {
            if (!_categoryProductCountGauges.ContainsKey(category))
            {
                var gauge = _meter.CreateObservableGauge(
                    name: $"product.count.by.category",
                    observeValue: () => GetProductCountByCategory(category),
                    unit: "{products}",
                    description: $"Number of products in category: {category}");
                
                _categoryProductCountGauges[category] = gauge;
                _logger.LogInformation("Created product count gauge for category {Category}", category);
            }
        }
    }

    private IEnumerable<Measurement<int>> GetProductCountByCategory(string category)
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            var count = dbContext.Products.Count(p => p.Category == category);
            
            yield return new Measurement<int>(count, new KeyValuePair<string, object?>("category", category));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product count for category {Category}", category);
            yield return new Measurement<int>(0, new KeyValuePair<string, object?>("category", category));
        }
    }

    // Track active products per category with an up-down counter
    private void UpdateActiveProductCounter(string category, long delta)
    {
        lock (_lock)
        {
            if (!_activeProductsCounters.TryGetValue(category, out var counter))
            {
                counter = _meter.CreateUpDownCounter<long>(
                    name: "business.product.active_by_category",
                    unit: "{products}",
                    description: "Number of active products by category");
                    
                _activeProductsCounters[category] = counter;
            }
            
            counter.Add(delta, new KeyValuePair<string, object?>("category", category));
        }
    }

    private void EnsureCategoryPriceGaugeExists(string category)
    {
        if (string.IsNullOrEmpty(category)) return;
        
        lock (_lock)
        {
            if (!_categoryPriceGauges.ContainsKey(category))
            {
                var gauge = _meter.CreateObservableGauge<double>(
                    name: $"business.product.avg_price_by_category",
                    observeValue: () => GetAveragePriceByCategory(category),
                    unit: "{currency}",
                    description: $"Average price of products in category: {category}");
                
                _categoryPriceGauges[category] = gauge;
                _logger.LogInformation("Created product price gauge for category {Category}", category);
            }
        }
    }

    private IEnumerable<Measurement<double>> GetAveragePriceByCategory(string category)
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            
            var avgPrice = dbContext.Products
                .Where(p => p.Category == category)
                .Average(p => Convert.ToDouble(p.Price));
                
            yield return new Measurement<double>(avgPrice, 
                new KeyValuePair<string, object?>("category", category));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting average price for category {Category}", category);
            yield return new Measurement<double>(0, 
                new KeyValuePair<string, object?>("category", category));
        }
    }

    // Calculate product catalog health
    private IEnumerable<Measurement<double>> GetProductCatalogHealth()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            
            var totalProducts = dbContext.Products.Count();
            if (totalProducts == 0)
                yield return new Measurement<double>(0);
            else
            {
                var productsWithStock = dbContext.Products.Count(p => p.StockQuantity > 0);
                var healthPercent = (double)productsWithStock / totalProducts * 100;
                yield return new Measurement<double>(healthPercent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating product catalog health");
            yield return new Measurement<double>(0);
        }
    }

    // Get popular categories based on view counts (simplified)
    private IEnumerable<Measurement<double>> GetPopularCategories()
    {
        // In a real application, this would query a database or cache
        // that keeps track of view counts by category.
        // Here we're generating a simplified example.
        
        var categories = new[] { "Electronics", "Home Appliances", "Other" };
        var viewCounts = new[] { 75.0, 20.0, 5.0 };  // Mock data
        
        for (int i = 0; i < categories.Length; i++)
        {
            yield return new Measurement<double>(
                viewCounts[i], 
                new KeyValuePair<string, object?>("category", categories[i]));
        }
    }

    // Get price volatility (simplified)
    private IEnumerable<Measurement<double>> GetPriceVolatility()
    {
        try
        {
            using var scope = Program.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            
            // In a real application, this would analyze price change history
            // Here we're just using a placeholder value
            
            // Simplified - assume 5% volatility
            yield return new Measurement<double>(5.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating price volatility");
            yield return new Measurement<double>(0);
        }
    }
}
