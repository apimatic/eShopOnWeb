namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Customer as returned by the Maxio API (spec schema "Customer"; serialized snake_case).
/// Only the fields this integration consumes are modeled.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// Spec schema "Customer-Response": wraps a customer in a top-level "customer" property.
/// </summary>
public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Spec schemas "Create-Customer" / "Customer-Attributes": fields accepted when creating a customer.
/// </summary>
public class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// Spec schema "Create-Customer-Request".
/// </summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}
