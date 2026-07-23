using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing-provider-side customer record that an eShopOnWeb user maps onto.
/// </summary>
public sealed class BillingCustomer
{
    public BillingCustomer(long id, string reference, string email, string? firstName, string? lastName)
    {
        Id = Guard.Against.NegativeOrZero(id, nameof(id));
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public long Id { get; }

    /// <summary>
    /// The stable eShopOnWeb reference (the user's username/email). Subscribing is idempotent on this value.
    /// </summary>
    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
