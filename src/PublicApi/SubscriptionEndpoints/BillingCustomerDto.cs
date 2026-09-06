namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The billing-provider customer an eShopOnWeb user is enrolled as.
/// </summary>
public class BillingCustomerDto
{
    /// <summary>The billing provider's customer id.</summary>
    public int Id { get; set; }

    /// <summary>The reference this application stores on the provider to identify the user.</summary>
    public string? Reference { get; set; }

    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Organization { get; set; }
}
