using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// The eShopOnWeb user, as the billing layer needs to see them. <see cref="Reference"/> is the
/// stable, unique identity (the authenticated user's name) that links the eShop user to their
/// Maxio customer record — it is used as the Maxio customer <c>reference</c>, which makes
/// "ensure a customer exists" idempotent across calls and process restarts.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable unique identity of the eShop user (their user name). Maxio customer <c>reference</c>.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
