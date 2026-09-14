namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for Create Customer (POST /customers.json).
/// Wire shape: <c>{ "customer": { ... } }</c> per Create-Customer-Request.yaml.
/// </summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer? Customer { get; set; }
}

/// <summary>
/// The customer attributes accepted when creating a customer (Create-Customer.yaml).
/// Only the fields this integration populates are surfaced.
/// </summary>
public class MaxioCreateCustomer
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    public string? Reference { get; set; }
}
