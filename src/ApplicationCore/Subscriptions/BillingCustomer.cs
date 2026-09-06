namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing-system representation of an eShopOnWeb shopper (a Maxio <c>Customer</c>).
/// </summary>
public sealed class BillingCustomer
{
    public required long Id { get; init; }

    /// <summary>
    /// The value eShopOnWeb owns and Maxio stores verbatim, used to correlate the shopper with the
    /// billing system. See <see cref="SubscriberIdentity.BillingReference"/>.
    /// </summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
