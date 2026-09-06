namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The billing-system customer an eShopOnWeb account maps onto.
/// </summary>
public class BillingCustomerDto
{
    public int Id { get; set; }

    /// <summary>Deterministic reference derived from the eShopOnWeb user name.</summary>
    public string? Reference { get; set; }

    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}
