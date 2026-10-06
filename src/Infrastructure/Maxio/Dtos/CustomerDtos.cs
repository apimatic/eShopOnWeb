namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Request body for POST /customers.json.</summary>
public class CreateCustomerRequest
{
    public MaxioCustomerDto Customer { get; set; } = new();
}

/// <summary>Response body for POST /customers.json and GET /customers/lookup.json.</summary>
public class CustomerResponse
{
    public MaxioCustomerDto Customer { get; set; } = new();
}

/// <summary>Response body for GET /customers.json (array of wrapped customers).</summary>
public class CustomerListResponse
{
    public MaxioCustomerDto Customer { get; set; } = new();
}
