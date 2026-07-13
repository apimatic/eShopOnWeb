using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(long subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
    }

    public SubscriptionNotFoundException(string customerReference)
        : base($"No active subscription found for customer reference '{customerReference}'")
    {
    }
}
