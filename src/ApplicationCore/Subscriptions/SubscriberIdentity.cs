using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user, as needed to ensure a matching Maxio customer exists.
/// <see cref="Reference"/> is the stable, unique idempotency key (the JWT identity /
/// username, which in eShopOnWeb is the user's email); it maps 1:1 to a Maxio customer.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds an identity from the authenticated username (the JWT name claim), which in
    /// eShopOnWeb is the user's email. Names are derived from the local part so a Maxio
    /// customer can be created without extra profile data.
    /// </summary>
    public static SubscriberIdentity FromUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user identity is required to subscribe.", nameof(userName));
        }

        var reference = userName.Trim();
        var localPart = reference.Contains('@') ? reference[..reference.IndexOf('@')] : reference;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? reference : localPart;

        return new SubscriberIdentity(
            reference: reference,
            email: reference,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }
}
