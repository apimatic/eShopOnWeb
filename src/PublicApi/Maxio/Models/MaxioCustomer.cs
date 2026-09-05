namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Mirrors the relevant fields of the Maxio "Customer" schema (maxio-spec/components/schemas/Customer.yaml).
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}
