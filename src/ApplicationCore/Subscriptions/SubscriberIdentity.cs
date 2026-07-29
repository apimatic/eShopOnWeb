using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of an eShopOnWeb user as seen by the billing system. This is the
/// bridge between an authenticated application user and their Maxio customer record.
/// <see cref="Reference"/> is the stable, unique key we store on the Maxio customer
/// (via its <c>reference</c> field) so that the same user always maps to the same
/// customer — the foundation of idempotent enrollment.
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
    }

    /// <summary>Stable, unique identifier of the user within eShopOnWeb (the authenticated user name).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
