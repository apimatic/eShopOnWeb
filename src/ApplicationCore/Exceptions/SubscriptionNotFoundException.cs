using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription with id {subscriptionId} was found.")
    {
    }
}
