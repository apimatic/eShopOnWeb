namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing-provider customer record that an eShopOnWeb user maps onto.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }

    /// <summary>The eShopOnWeb user reference this customer was created for.</summary>
    public string? Reference { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
