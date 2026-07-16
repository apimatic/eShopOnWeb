using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The proration amounts shown to the customer no longer match a fresh preview taken at commit time
/// (UC3: "never silently apply a different amount than the one shown"). The caller must re-preview.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId, PlanChangePreview expected, PlanChangePreview actual)
        : base($"The previewed amount for subscription {subscriptionId} is no longer current; request a fresh preview before committing.")
    {
        SubscriptionId = subscriptionId;
        ExpectedPreview = expected;
        ActualPreview = actual;
    }

    public int SubscriptionId { get; }
    public PlanChangePreview ExpectedPreview { get; }
    public PlanChangePreview ActualPreview { get; }
}
