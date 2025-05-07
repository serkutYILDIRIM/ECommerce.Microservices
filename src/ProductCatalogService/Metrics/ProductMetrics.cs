using System.Diagnostics.Metrics;
using ProductCatalogService.Models;
using Shared.Library.Metrics;
using ProductCatalogService.Data; // Add missing reference for ProductDbContext

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
    private readonly ILogger<ProductMetrics> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ProductMetrics(MeterProvider meterProvider, ILogger<ProductMetrics> logger, IServiceProvider serviceProvider)
    {
        _meter = meterProvider.AppMeter;
        _logger = logger;
        _serviceProvider = serviceProvider;

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
            observeValue: () => GetTotalProductCount(),
            unit: "{products}",
            description: "Total number of products in the catalog");

        _logger.LogInformation("Product metrics initialized");
    }

    public void RecordProductView(Product product)
    {
        _productViewsCounter.Add(1, new KeyValuePair<string, object?>("product.id", product.Id),
                                   new KeyValuePair<string, object?>("product.name", product.Name),
                                   new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordProductCreation(Product product)
    {
        _productCreationsCounter.Add(1, new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordProductUpdate(Product product)
    {
        _productUpdatesCounter.Add(1, new KeyValuePair<string, object?>("product.id", product.Id),
                                     new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordProductDeletion(Product product)
    {
        _productDeletionsCounter.Add(1, new KeyValuePair<string, object?>("product.category", product.Category));
    }

    public void RecordSearchDuration(double durationMs, string searchTerm, int resultCount)
    {
        _searchDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("search.term", searchTerm),
            new KeyValuePair<string, object?>("search.result_count", resultCount));
    }

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

    private int GetTotalProductCount()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            return dbContext.Products.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total product count for metrics");
            return 0;
        }
    }
}
