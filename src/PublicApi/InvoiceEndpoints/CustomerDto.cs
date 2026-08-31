namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Customer/payer details carried on a bill.</summary>
public class CustomerDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
}
