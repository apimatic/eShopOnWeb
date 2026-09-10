using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user, projected into the fields Maxio needs to identify a customer.
/// <see cref="Reference"/> is the stable idempotency key that links a single eShopOnWeb user to a
/// single Maxio customer: subscribing is keyed off it so a double-click never creates two customers.
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

    /// <summary>Stable per-user identifier stored as the Maxio customer <c>reference</c>.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds a subscriber identity from an authenticated eShopOnWeb user name (which is the user's
    /// email). The user name is used verbatim as both the email and the Maxio customer reference so
    /// the mapping survives even though the in-memory database loses local state on restart.
    /// </summary>
    public static SubscriberIdentity FromUserName(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var localPart = userName.Contains('@') ? userName[..userName.IndexOf('@')] : userName;
        string firstName;
        string lastName;
        if (localPart.Contains('.'))
        {
            var pieces = localPart.Split('.', 2);
            firstName = Capitalize(pieces[0]);
            lastName = Capitalize(pieces[1]);
        }
        else
        {
            firstName = Capitalize(localPart);
            lastName = "eShopOnWeb";
        }

        return new SubscriberIdentity(reference: userName, email: userName, firstName: firstName, lastName: lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "eShop";
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
