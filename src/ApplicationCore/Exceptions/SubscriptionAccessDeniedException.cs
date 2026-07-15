using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId) : base($"Subscription {subscriptionId} does not belong to the requesting user")
    {
    }
}
