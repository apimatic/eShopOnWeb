namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>Request body for POST /customers.json.</summary>
public class CreateMaxioCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

public class MaxioCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}
