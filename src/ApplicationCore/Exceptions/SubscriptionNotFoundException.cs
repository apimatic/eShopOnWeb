using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription cannot be found, or when it exists but does not belong to the user
/// making the request. Both cases produce the same message so that the endpoint surface does not
/// let one customer probe for another customer's subscription ids.
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
