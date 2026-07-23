namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user, keyed by <see cref="Reference"/>.
/// </summary>
public sealed record BillingCustomer
{
    /// <summary>Provider-assigned customer id.</summary>
    public required int Id { get; init; }

    /// <summary>
    /// The eShopOnWeb user's email/username. Unique per customer at the provider, which is what makes
    /// "ensure a customer exists" idempotent across repeated subscribe attempts (plan.md §4.4).
    /// </summary>
    public required string Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
