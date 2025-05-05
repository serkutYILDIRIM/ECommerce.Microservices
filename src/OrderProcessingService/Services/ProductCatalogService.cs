using System.Diagnostics;
using System.Net.Http.Json;
using OrderProcessingService.Models;
using OrderProcessingService.Telemetry;
using Shared.Library.Telemetry;

namespace OrderProcessingService.Services;

public interface IProductCatalogService
{
    Task<ProductDto?> GetProductAsync(int productId);
    Task<bool> UpdateProductStockAsync(int productId, int quantityToReduce);
}

public class ProductCatalogService : IProductCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductCatalogService> _logger;
    private readonly HttpClientContextPropagator _propagator;

    public ProductCatalogService(
        HttpClient httpClient, 
        ILogger<ProductCatalogService> logger,
        HttpClientContextPropagator propagator)
    {
        _httpClient = httpClient;
        _logger = logger;
        _propagator = propagator;
    }

    public async Task<ProductDto?> GetProductAsync(int productId)
    {
        using var activity = TelemetryConfig.ActivitySource.StartActivity("ProductCatalogService.GetProduct");
        activity?.SetTag("product.id", productId);
        
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/products/{productId}");
            _propagator.EnrichRequest(request, activity);
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP status code: {response.StatusCode}");
                _logger.LogWarning("Failed to get product {ProductId}: HTTP status code {StatusCode}", 
                    productId, response.StatusCode);
                return null;
            }
            
            var product = await response.Content.ReadFromJsonAsync<ProductDto>();
            
            if (product != null)
            {
                activity?.SetTag("product.found", true);
                activity?.SetTag("product.name", product.Name);
            }
            else
            {
                activity?.SetTag("product.found", false);
                _logger.LogWarning("Product {ProductId} not found", productId);
            }
            
            return product;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error getting product {ProductId}", productId);
            return null;
        }
    }

    public async Task<bool> UpdateProductStockAsync(int productId, int quantityToReduce)
    {
        using var activity = TelemetryConfig.ActivitySource.StartActivity("ProductCatalogService.UpdateStock");
        activity?.SetTag("product.id", productId);
        activity?.SetTag("product.quantity_change", -quantityToReduce);
        
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/products/{productId}/reducestock");
            _propagator.EnrichRequest(request, activity);
            
            // Create the payload
            var payload = new { quantity = quantityToReduce };
            request.Content = JsonContent.Create(payload);
            
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP status code: {response.StatusCode}");
                _logger.LogWarning("Failed to update stock for product {ProductId}: HTTP status code {StatusCode}", 
                    productId, response.StatusCode);
                return false;
            }
            
            _logger.LogInformation("Successfully updated stock for product {ProductId}, reduced by {Quantity}", 
                productId, quantityToReduce);
            return true;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error updating stock for product {ProductId}", productId);
            return false;
        }
    }
}
