using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The pricing shown to the customer during UC3's preview step no longer matches what a fresh
/// preview computes at commit time. Per plan.md UC3's failure scenarios, the commit must be rejected
/// and a fresh preview required — the system must never silently apply a different amount than the
/// one the customer confirmed.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The plan-change preview for subscription {subscriptionId} is stale; request a fresh preview before committing.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
