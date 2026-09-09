using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a billing operation is performed on behalf of.
/// The identity is derived entirely from the caller's authenticated token, never from
/// request input, so a caller can only ever act on their own billing account.
/// </summary>
public sealed class SubscriberIdentity
{
    private SubscriberIdentity(string email, string reference, string firstName, string lastName)
    {
        Email = email;
        Reference = reference;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The user's email address (the eShopOnWeb username).</summary>
    public string Email { get; }

    /// <summary>
    /// The stable, unique identifier stored on the Maxio customer record so the same
    /// eShopOnWeb user always maps to the same Maxio customer (idempotency key).
    /// </summary>
    public string Reference { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds an identity from the authenticated user's email. The email doubles as the
    /// Maxio customer <c>reference</c>, which keeps the mapping stable across app restarts
    /// even though the local (in-memory) store does not persist it.
    /// </summary>
    public static SubscriberIdentity FromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An authenticated email is required to identify the subscriber.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant();
        var localPart = normalized.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new SubscriberIdentity(normalized, normalized, firstName, "eShopOnWeb Subscriber");
    }
}
