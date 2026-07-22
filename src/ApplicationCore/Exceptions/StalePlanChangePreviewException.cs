using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration quoted to the customer no longer matches what the provider would
/// charge. The change is refused so the customer is never billed an amount they were not shown
/// (UC3 failure scenario).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base($"The plan change preview for subscription {subscriptionId} is no longer accurate. " +
               "Request a fresh preview and confirm again.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
