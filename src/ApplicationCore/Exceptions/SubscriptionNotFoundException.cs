using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId} for this customer")
    {
        SubscriptionId = subscriptionId;
    }

    public SubscriptionNotFoundException(string userName)
        : base($"No active subscription found for {userName}")
    {
    }

    public int? SubscriptionId { get; }
}
