using System.Diagnostics;
using System.Net.Http.Json;
using OrderProcessingService.Models;
using OrderProcessingService.Telemetry;
using Shared.Library.Telemetry;

namespace OrderProcessingService.Services;

public class ProductCatalogService
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
            }
            
            return product;
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            _logger.LogError(ex, "Error fetching product with ID {ProductId}", productId);
            return null;
        }
    }

    public async Task<bool> UpdateProductStockAsync(int productId, int quantityToReduce)
    {
        using var activity = TelemetryConfig.ActivitySource.StartActivity("ProductCatalogService.UpdateProductStock");
        activity?.SetTag("product.id", productId);
        activity?.SetTag("quantity.reduce", quantityToReduce);
        
        try
        {
            // First get the product
            activity?.AddEvent(new ActivityEvent("FetchingProduct"));
            var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/products/{productId}");
            _propagator.EnrichRequest(getRequest, activity);
            
            var getResponse = await _httpClient.SendAsync(getRequest);
            if (!getResponse.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP status code: {getResponse.StatusCode}");
                return false;
            }
            
            var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
            
            if (product == null)
            {
                activity?.SetTag("product.found", false);
                return false;
            }
            
            activity?.SetTag("product.found", true);
            activity?.SetTag("product.current_stock", product.StockQuantity);

            // Update stock quantity
            product.StockQuantity -= quantityToReduce;
            if (product.StockQuantity < 0)
            {
                activity?.SetTag("product.sufficient_stock", false);
                return false;
            }
            
            activity?.SetTag("product.new_stock", product.StockQuantity);
            activity?.SetTag("product.sufficient_stock", true);
            activity?.AddEvent(new ActivityEvent("UpdatingProduct"));

            // Send PUT request to update product
            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/products/{productId}")
            {
                Content = JsonContent.Create(product)
            };
            _propagator.EnrichRequest(putRequest, activity);
            
            var response = await _httpClient.SendAsync(putRequest);
            
            var success = response.IsSuccessStatusCode;
            activity?.SetTag("update.success", success);
            return success;
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            _logger.LogError(ex, "Error updating stock for product {ProductId}", productId);
            return false;
        }
    }
}
