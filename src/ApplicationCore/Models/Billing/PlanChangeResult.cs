using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The outcome of a committed plan change (UC3): where the subscription came from, where it landed,
/// and when the move takes effect.
/// </summary>
public class PlanChangeResult
{
    public PlanChangeResult(Subscription subscription, string oldPlanHandle, PlanChangeTiming timing, DateTimeOffset? effectiveAt)
    {
        Subscription = subscription;
        OldPlanHandle = oldPlanHandle;
        Timing = timing;
        EffectiveAt = effectiveAt;
    }

    public Subscription Subscription { get; }
    public string OldPlanHandle { get; }
    public string NewPlanHandle => Subscription.PlanHandle;
    public PlanChangeTiming Timing { get; }

    /// <summary>When the new plan starts applying - now for a prorated change, at renewal otherwise.</summary>
    public DateTimeOffset? EffectiveAt { get; }
}
