using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a committed plan change (UC3 step 5). Published in-process after the provider
/// accepted the change; delivery is best-effort (§2.5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string buyerId,
        long subscriptionId,
        string previousPlanHandle,
        string newPlanHandle,
        PlanChangeTiming timing,
        int paymentDueInCents,
        DateTimeOffset? effectiveAt)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
        PaymentDueInCents = paymentDueInCents;
        EffectiveAt = effectiveAt;
    }

    public string BuyerId { get; }

    public long SubscriptionId { get; }

    public string PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The previewed amount the customer confirmed, in minor units.</summary>
    public int PaymentDueInCents { get; }

    public DateTimeOffset? EffectiveAt { get; }
}
