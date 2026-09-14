namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing-system record that an eShopOnWeb user maps onto, keyed by <see cref="Reference"/>.
/// </summary>
public sealed record BillingCustomer
{
    public required long Id { get; init; }

    /// <summary>Our own identifier for the customer, see <see cref="BillingReferences"/>.</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
