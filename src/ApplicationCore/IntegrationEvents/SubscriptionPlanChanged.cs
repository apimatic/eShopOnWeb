using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription moves to a different plan (UC3 step 5).
/// Delivery is best-effort (plan.md §2.5).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription, string previousPlanHandle,
        PlanChangeTiming timing, PlanChangePreview appliedPreview)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Timing = timing;
        AppliedPreview = appliedPreview;
    }

    public Subscription Subscription { get; }
    public string PreviousPlanHandle { get; }
    public PlanChangeTiming Timing { get; }
    public PlanChangePreview AppliedPreview { get; }
}
