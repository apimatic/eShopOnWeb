using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a non-admin user requests an action on a subscription they do not own.</summary>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId) : base($"Subscription {subscriptionId} does not belong to the requesting user")
    {
    }
}
