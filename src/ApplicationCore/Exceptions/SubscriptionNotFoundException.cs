using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// No subscription with the requested id exists at the billing provider.
/// </summary>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
