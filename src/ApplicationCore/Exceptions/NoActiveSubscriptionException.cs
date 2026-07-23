using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The user has no subscription the provider is currently billing, so there is nothing for the
/// requested action to apply to. Thrown before any provider call is made.
/// </summary>
public class NoActiveSubscriptionException : Exception
{
    public NoActiveSubscriptionException(string userReference)
        : base($"No active subscription found for {userReference}")
    {
        UserReference = userReference;
    }

    public string UserReference { get; }
}
