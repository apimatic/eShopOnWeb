namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Mirrors components/schemas/Customer-Response.yaml
public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

// Mirrors components/schemas/Customer.yaml (fields relevant to this integration)
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

// Mirrors components/schemas/Create-Customer-Request.yaml
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

// Mirrors components/schemas/Create-Customer.yaml
public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}
