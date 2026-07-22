using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested subscription does not exist on the billing provider, or does not belong to the
/// user making the request.
/// </summary>
/// <remarks>
/// The message is deliberately identical in both cases so that an authenticated user cannot probe
/// for the existence of another user's subscription ids.
/// </remarks>
public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(int subscriptionId)
        : base($"No subscription found with id {subscriptionId}")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
