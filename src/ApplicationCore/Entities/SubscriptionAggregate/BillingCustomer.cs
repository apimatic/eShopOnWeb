namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing-system representation of an eShopOnWeb user.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }

    /// <summary>
    /// The value that ties this billing customer back to an eShopOnWeb user.
    /// See <see cref="BillingCustomerReference"/>.
    /// </summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
