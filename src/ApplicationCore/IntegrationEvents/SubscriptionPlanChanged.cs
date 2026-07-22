using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan (UC3 step 5). Published in-process
/// and best-effort after the provider call succeeds (§2.5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription, string previousPlanHandle,
        PlanChangeTiming timing, PlanChangePreview? appliedPreview)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Timing = timing;
        AppliedPreview = appliedPreview;
    }

    public Subscription Subscription { get; }

    public string PreviousPlanHandle { get; }

    public string NewPlanHandle => Subscription.Plan.Handle;

    public PlanChangeTiming Timing { get; }

    /// <summary>The proration that was quoted and applied, when the change was previewed.</summary>
    public PlanChangePreview? AppliedPreview { get; }
}
