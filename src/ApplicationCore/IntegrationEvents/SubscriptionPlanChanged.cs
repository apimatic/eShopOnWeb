using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan (UC3, step 5).
/// </summary>
/// <remarks>
/// Published in-process through MediatR after the provider call has already succeeded. Delivery
/// is best-effort: a failing handler is logged and never rolls back the plan change.
/// </remarks>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(
        Subscription subscription,
        string previousPlanHandle,
        string newPlanHandle,
        PlanChangeTiming timing,
        decimal prorationAmount)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
        ProrationAmount = prorationAmount;
    }

    public Subscription Subscription { get; }

    public string PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The amount due for the change, in whole currency units, as previewed.</summary>
    public decimal ProrationAmount { get; }
}
