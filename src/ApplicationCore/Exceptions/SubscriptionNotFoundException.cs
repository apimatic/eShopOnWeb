using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId) : base($"No subscription found with id {subscriptionId}")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
