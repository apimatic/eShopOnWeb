using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity of the shopper being billed, projected onto the fields the billing
/// system of record needs.
/// </summary>
/// <param name="Reference">
/// Stable, never-reused application-side key for this shopper. The billing adapter turns it into the
/// provider's customer reference, which is the only idempotency key available — so it must survive
/// application restarts and must never be reassigned to a different person.
/// </param>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName)
{
    public static SubscriberIdentity Create(string reference, string email, string firstName, string lastName)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));
        Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));

        return new SubscriberIdentity(reference, email, firstName, lastName);
    }
}
