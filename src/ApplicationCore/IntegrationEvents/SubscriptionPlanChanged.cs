using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a subscription's plan is changed, now or scheduled (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string oldPlanHandle, string newPlanHandle, DateTimeOffset effectiveAt)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldPlanHandle = oldPlanHandle;
        NewPlanHandle = newPlanHandle;
        EffectiveAt = effectiveAt;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string OldPlanHandle { get; }
    public string NewPlanHandle { get; }
    public DateTimeOffset EffectiveAt { get; }
}
