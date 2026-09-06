namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing-provider customer record that an eShopOnWeb user maps onto.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }

    /// <summary>The deterministic reference this integration writes, derived from the eShopOnWeb user.</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
