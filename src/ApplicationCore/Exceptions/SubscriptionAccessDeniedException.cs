using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The subscription being acted on does not belong to the requesting (non-admin) user.</summary>
public class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException(int subscriptionId) : base($"Subscription {subscriptionId} does not belong to the requesting user.")
    {
    }
}
