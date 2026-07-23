using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The acting user is not the owner of the subscription and is not an administrator.
/// </summary>
/// <remarks>
/// The message deliberately does not reveal who owns the subscription.
/// </remarks>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId)
        : base($"Subscription {subscriptionId} does not belong to the current user.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
