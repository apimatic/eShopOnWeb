using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a Maxio customer/subscription should be tied to.
/// <see cref="Reference"/> is the stable, unique key stored on the Maxio customer record
/// (customer <c>reference</c>) so that the same eShop user always maps to a single Maxio customer.
/// </summary>
public sealed class MaxioCustomerIdentity
{
    public MaxioCustomerIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable unique identifier for the eShop user (used as the Maxio customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
