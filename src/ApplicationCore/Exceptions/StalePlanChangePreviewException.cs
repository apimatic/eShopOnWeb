using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration preview the customer confirmed no longer prices the change the same way, so the
/// commit was refused rather than charging an amount the customer never saw (UC3). The caller must
/// take a fresh preview and ask the customer to confirm again.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The plan change preview for subscription {subscriptionId} is no longer current. " +
               "Review the refreshed cost and confirm again.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
