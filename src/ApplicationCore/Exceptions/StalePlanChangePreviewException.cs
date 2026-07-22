using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The plan-change preview the customer confirmed no longer matches what the provider would charge
/// (UC3). The commit is rejected rather than silently applying an amount the customer never saw.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId)
        : base("The cost of this plan change has changed since it was previewed. " +
               "Review the new amount and confirm again.")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
