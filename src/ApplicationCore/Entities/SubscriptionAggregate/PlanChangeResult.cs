using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The committed outcome of a plan change: old plan, new plan, proration and effective date (UC3).
/// </summary>
public class PlanChangeResult
{
    public PlanChangeResult(Subscription subscription,
        string previousPlanHandle,
        PlanChangePreview preview,
        DateTimeOffset? effectiveAt)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Preview = preview;
        EffectiveAt = effectiveAt;
    }

    public Subscription Subscription { get; }
    public string PreviousPlanHandle { get; }
    public PlanChangePreview Preview { get; }
    public DateTimeOffset? EffectiveAt { get; }
}
