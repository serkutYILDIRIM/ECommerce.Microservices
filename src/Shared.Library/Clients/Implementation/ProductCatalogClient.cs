using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;

namespace Shared.Library.Clients.Implementation;

public class ProductCatalogClient : IProductCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductCatalogClient> _logger;

    public ProductCatalogClient(HttpClient httpClient, ILogger<ProductCatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        try
        {
            var products = await _httpClient.GetFromJsonAsync<List<ProductDto>>("/products");
            return products ?? new List<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products");
            return new List<ProductDto>();
        }
    }

    public async Task<ProductDto?> GetProductAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ProductDto>($"/products/{id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product with ID {ProductId}", id);
            return null;
        }
    }

    public async Task<ProductDto> CreateProductAsync(ProductDto product)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/products", product);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product");
            throw;
        }
    }

    public async Task<ProductDto?> UpdateProductAsync(int id, ProductDto product)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/products/{id}", product);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product with ID {ProductId}", id);
            return null;
        }
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/products/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product with ID {ProductId}", id);
            return false;
        }
    }
}
