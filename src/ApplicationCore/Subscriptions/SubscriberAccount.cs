using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity that a billing customer is created for.
/// </summary>
public class SubscriberAccount
{
    public SubscriberAccount(string accountKey, string email, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(accountKey)) throw new ArgumentException("An account key is required.", nameof(accountKey));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("An email address is required.", nameof(email));

        AccountKey = accountKey;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>
    /// Stable key identifying the shopper across application restarts. It is the value the billing
    /// customer record is keyed on, so it must not change for the lifetime of the account.
    /// </summary>
    public string AccountKey { get; }

    public string Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
}
