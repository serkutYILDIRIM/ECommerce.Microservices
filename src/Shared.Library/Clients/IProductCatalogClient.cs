using Shared.Library.Models;

namespace Shared.Library.Clients;

public interface IProductCatalogClient
{
    Task<List<ProductDto>> GetProductsAsync();
    Task<ProductDto?> GetProductAsync(int id);
    Task<ProductDto> CreateProductAsync(ProductDto product);
    Task<ProductDto?> UpdateProductAsync(int id, ProductDto product);
    Task<bool> DeleteProductAsync(int id);
}
