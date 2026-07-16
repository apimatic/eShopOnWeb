using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a non-admin caller attempts to act on a subscription that does not belong to them.
/// </summary>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId) : base($"Subscription {subscriptionId} does not belong to the current user")
    {
    }
}
