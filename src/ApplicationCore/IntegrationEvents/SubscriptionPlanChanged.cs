using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan (UC3, step 5), carrying the plan it
/// came from so handlers can report old plan → new plan.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription, string previousPlanHandle, PlanChangeTiming timing)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Timing = timing;
    }

    public Subscription Subscription { get; }
    public string PreviousPlanHandle { get; }
    public PlanChangeTiming Timing { get; }
}
