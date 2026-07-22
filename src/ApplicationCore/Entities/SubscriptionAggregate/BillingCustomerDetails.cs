namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The details needed to create a provider-side customer for an eShopOnWeb user.
/// </summary>
public sealed record BillingCustomerDetails
{
    /// <summary>
    /// The stable eShopOnWeb user reference (email/username). Used as the provider-side
    /// customer reference so that customer creation is idempotent.
    /// </summary>
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }
}
