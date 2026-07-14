using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a non-admin caller requests a subscription that does not belong to them.
/// </summary>
public class UnauthorizedSubscriptionAccessException : Exception
{
    public UnauthorizedSubscriptionAccessException(int subscriptionId)
        : base($"Subscription {subscriptionId} does not belong to the requesting customer.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
