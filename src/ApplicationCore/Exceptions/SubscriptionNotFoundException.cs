using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a subscription does not exist, or does not belong to the acting user.</summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId} for this user")
    {
    }
}
