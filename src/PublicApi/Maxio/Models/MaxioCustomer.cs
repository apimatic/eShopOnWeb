namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A Maxio Customer. Mirrors the subset of Customer.yaml used by the integration.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    public string? Reference { get; set; }
}
