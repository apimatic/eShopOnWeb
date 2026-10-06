namespace Maxio.Models;

/// <summary>
/// Request body for POST /customers.json. Mirrors the Create Customer Request schema in the Maxio OpenAPI specification.
/// </summary>
public class CreateCustomerRequest
{
    public CreateCustomer Customer { get; set; } = new();
}
