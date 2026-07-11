using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The acting user does not own the subscription and is not an administrator.</summary>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId) : base($"Subscription {subscriptionId} does not belong to the current user")
    {
    }
}
