using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity that a billing customer is created for. Built from the caller's
/// authentication token, never from request input, so a caller can only ever act on themselves.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string? email = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email.Trim();
    }

    /// <summary>
    /// The eShopOnWeb user name. This is the stable key the billing customer is keyed on.
    /// </summary>
    public string UserName { get; }

    /// <summary>
    /// The e-mail address to register with the billing provider. Defaults to <see cref="UserName"/>,
    /// which is an e-mail address for every eShopOnWeb account.
    /// </summary>
    public string Email { get; }
}
