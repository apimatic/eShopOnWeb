using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Identifies the eShopOnWeb user that a subscription belongs to. The <see cref="Reference"/>
/// is the stable, unique key that ties an eShopOnWeb user to a customer record in the billing
/// system, and is what makes "ensure a customer exists" idempotent.
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

    /// <summary>The unique identifier for this user within eShopOnWeb (used as the billing customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds an identity from an authenticated user's email (the eShopOnWeb username). The email
    /// doubles as the stable customer reference; a best-effort first/last name is derived from it
    /// because eShopOnWeb does not capture names, and the billing system requires them.
    /// </summary>
    public static SubscriberIdentity FromEmail(string email)
    {
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        var localPart = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? email : localPart;

        return new SubscriberIdentity(
            reference: email,
            email: email,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
