using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a plan-change commit's expected proration amount no longer matches a freshly
/// computed preview. Prevents applying a different amount than the one the customer confirmed.
/// </summary>
public class PlanChangePreviewStaleException : Exception
{
    public PlanChangePreviewStaleException(int subscriptionId)
        : base($"The proration preview for subscription {subscriptionId} is stale; request a fresh preview before committing.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
