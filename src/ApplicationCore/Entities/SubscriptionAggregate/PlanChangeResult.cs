using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The committed outcome of a UC3 plan change: what the subscription moved from, what it moved to,
/// the proration that was applied, and when it takes effect.
/// </summary>
public sealed record PlanChangeResult
{
    public PlanChangeResult(CustomerSubscription subscription,
        string previousPlanHandle,
        string newPlanHandle,
        PlanChangeTiming timing,
        PlanChangePreview appliedPreview)
    {
        Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
        AppliedPreview = appliedPreview ?? throw new ArgumentNullException(nameof(appliedPreview));
    }

    public CustomerSubscription Subscription { get; init; }

    public string PreviousPlanHandle { get; init; }

    public string NewPlanHandle { get; init; }

    public PlanChangeTiming Timing { get; init; }

    /// <summary>The freshly re-computed preview that was verified against the customer's confirmation and then applied.</summary>
    public PlanChangePreview AppliedPreview { get; init; }

    /// <summary>Proration applied by the change, in dollars.</summary>
    public decimal ProrationAmount => AppliedPreview.ProratedAdjustment;

    /// <summary>
    /// When the change takes effect: immediately (null) or at the subscription's next renewal.
    /// </summary>
    public DateTimeOffset? EffectiveAt => Timing == PlanChangeTiming.Immediately
        ? null
        : Subscription.NextBillingDate;
}
