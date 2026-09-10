using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper who is subscribing. Built from the authenticated
/// caller's identity (their token), never from client-supplied input. The <see cref="Reference"/>
/// is the stable key used to link the eShopOnWeb user to a single Maxio customer record.
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

    /// <summary>Stable unique identifier for this shopper in eShopOnWeb (used as the Maxio customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds a subscriber identity from an eShopOnWeb username (which, in this app, is the user's email).
    /// Maxio requires first/last name on customer creation, so we derive a sensible display name from the
    /// email local-part when nothing better is available.
    /// </summary>
    public static SubscriberIdentity FromUsername(string username)
    {
        Guard.Against.NullOrWhiteSpace(username, nameof(username));

        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? username : localPart;

        return new SubscriberIdentity(
            reference: username,
            email: username,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
