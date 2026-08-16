using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of the eShopOnWeb user on whose behalf a billing operation is performed.
/// Built from the authenticated caller (their JWT), never from client-supplied input.
/// </summary>
public class SubscriberInfo
{
    public SubscriberInfo(string userName, string email, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));

        UserName = userName;
        Email = string.IsNullOrWhiteSpace(email) ? userName : email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The eShopOnWeb user name (stable across restarts; used to derive the billing reference).</summary>
    public string UserName { get; }

    /// <summary>The user's email address.</summary>
    public string Email { get; }

    /// <summary>A first name for the billing customer record (derived when the app has none).</summary>
    public string FirstName { get; }

    /// <summary>A last name for the billing customer record (derived when the app has none).</summary>
    public string LastName { get; }

    /// <summary>
    /// Builds a <see cref="SubscriberInfo"/> from an authenticated identity. eShopOnWeb users
    /// have no first/last name, so they are derived from the email/user name for a readable
    /// billing record.
    /// </summary>
    public static SubscriberInfo FromIdentity(string userName, string? email)
    {
        var effectiveEmail = string.IsNullOrWhiteSpace(email) ? userName : email!;
        var localPart = effectiveEmail.Contains('@')
            ? effectiveEmail[..effectiveEmail.IndexOf('@')]
            : effectiveEmail;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new SubscriberInfo(userName, effectiveEmail, firstName, "eShopOnWeb");
    }
}
