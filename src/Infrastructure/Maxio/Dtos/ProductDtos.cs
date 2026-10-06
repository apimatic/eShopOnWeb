namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Response body for GET /product_families/{id}/products.json (array of wrapped products).</summary>
public class ProductListResponse
{
    public MaxioProductDto Product { get; set; } = new();
}

/// <summary>Response body for GET /products/handle/{handle}.json.</summary>
public class ProductResponse
{
    public MaxioProductDto Product { get; set; } = new();
}
