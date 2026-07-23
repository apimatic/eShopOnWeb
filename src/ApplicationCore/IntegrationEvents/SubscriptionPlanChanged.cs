using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved to a different plan. Published in-process, best-effort,
/// only after the provider committed the change.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference,
        BillingSubscription subscription,
        string? previousPlanHandle,
        PlanChangeTiming timing)
    {
        UserReference = userReference;
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        Timing = timing;
    }

    public string UserReference { get; }

    public BillingSubscription Subscription { get; }

    public string? PreviousPlanHandle { get; }

    /// <summary>Whether the change applied immediately or was scheduled for the next renewal.</summary>
    public PlanChangeTiming Timing { get; }

    /// <summary>
    /// The plan the subscription is on (immediate change) or will move to (deferred change).
    /// </summary>
    public string? NewPlanHandle => Timing == PlanChangeTiming.AtNextRenewal
        ? Subscription.NextPlanHandle
        : Subscription.PlanHandle;
}
