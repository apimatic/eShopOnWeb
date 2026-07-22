using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the proration quoted at preview time no longer matches the provider's quote at
/// commit time. The change is refused rather than silently charging a different amount (UC3).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The plan change preview for subscription {subscriptionId} is no longer valid. " +
               "Request a fresh preview and confirm the current amount.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
