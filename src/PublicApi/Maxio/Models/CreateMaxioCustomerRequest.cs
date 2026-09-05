namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for POST /customers.json (maxio-spec/components/schemas/Create-Customer.yaml).
/// </summary>
public class CreateMaxioCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Must be unique per Maxio site. eShopOnWeb uses the caller's username (email) as the
    /// reference so a given eShopOnWeb user always maps to exactly one Maxio customer.
    /// </summary>
    public string Reference { get; set; } = string.Empty;
}

public class CreateMaxioCustomerEnvelope
{
    public CreateMaxioCustomerRequest Customer { get; set; } = new();
}
