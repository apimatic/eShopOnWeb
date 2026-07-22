using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription moves to another plan (UC3 step 5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(int subscriptionId,
        string previousPlanHandle,
        string newPlanHandle,
        decimal prorationAmount,
        DateTimeOffset? effectiveAt)
    {
        SubscriptionId = subscriptionId;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        ProrationAmount = prorationAmount;
        EffectiveAt = effectiveAt;
    }

    public int SubscriptionId { get; }
    public string PreviousPlanHandle { get; }
    public string NewPlanHandle { get; }
    public decimal ProrationAmount { get; }
    public DateTimeOffset? EffectiveAt { get; }
}
