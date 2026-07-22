using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration basis moved between the preview the customer confirmed and the moment of
/// commit (UC3). The commit is refused rather than silently applying an amount the customer never saw;
/// the caller must obtain a fresh preview.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The previewed cost for changing subscription {subscriptionId} is no longer current. Review a fresh preview before confirming.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
