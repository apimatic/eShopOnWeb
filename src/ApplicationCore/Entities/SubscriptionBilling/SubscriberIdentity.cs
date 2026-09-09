using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Describes the eShopOnWeb user who is subscribing, in terms the billing system understands.
/// <para>
/// <see cref="Reference"/> is the stable, unique key that ties an eShopOnWeb user to their record in
/// the billing system. It must be derived from something that survives application restarts (the user's
/// email/username) rather than a per-run identifier, so that "ensure a customer exists" stays idempotent.
/// </para>
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique idempotency key for this subscriber in the billing system.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds a subscriber identity from an authenticated eShopOnWeb user name (which is the email).
    /// The billing system requires first/last name, which the default identity model does not store, so
    /// they are derived deterministically from the email local-part.
    /// </summary>
    public static SubscriberIdentity FromUser(string userName, string? email = null)
    {
        var resolvedEmail = string.IsNullOrWhiteSpace(email) ? userName : email;
        var reference = userName.Trim().ToLowerInvariant();

        var localPart = resolvedEmail.Contains('@')
            ? resolvedEmail[..resolvedEmail.IndexOf('@')]
            : resolvedEmail;
        var firstName = string.IsNullOrWhiteSpace(localPart)
            ? "eShopOnWeb"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(localPart);

        return new SubscriberIdentity(reference, resolvedEmail, firstName, "eShopOnWeb Customer");
    }
}
