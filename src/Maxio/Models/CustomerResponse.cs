namespace Maxio.Models;

/// <summary>
/// Response body for customer endpoints. Mirrors the Customer Response schema in the Maxio OpenAPI specification.
/// </summary>
public class CustomerResponse
{
    public Customer Customer { get; set; } = new();
}
