using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the amount shown in a plan-change preview no longer matches what would be
/// charged at commit time (§UC3) — the caller must request a fresh preview.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The plan-change preview for subscription {subscriptionId} is no longer valid; request a fresh preview before committing.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
