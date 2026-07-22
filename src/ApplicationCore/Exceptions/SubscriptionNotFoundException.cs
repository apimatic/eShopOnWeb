using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription cannot be found for the requesting user.
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription with id {subscriptionId} is available to this user.")
    {
    }
}
