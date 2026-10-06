namespace Maxio.Models;

/// <summary>
/// Response body for product endpoints. Mirrors the Product Response schema in the Maxio OpenAPI specification.
/// </summary>
public class ProductResponse
{
    public Product Product { get; set; } = new();
}
