using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId) : base($"No subscription found with id {subscriptionId}")
    {
    }

    public SubscriptionNotFoundException(string customerReference) : base($"No active subscription found for customer reference '{customerReference}'")
    {
    }
}
