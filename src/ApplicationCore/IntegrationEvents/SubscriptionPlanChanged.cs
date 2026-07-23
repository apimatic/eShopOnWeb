using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription has been moved to a different plan.
/// </summary>
/// <remarks>
/// Delivery is best-effort and in-process only; a handler failure never reverses the plan change.
/// </remarks>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(
        string userReference,
        BillingSubscription subscription,
        string? previousPlanHandle,
        string newPlanHandle,
        PlanChangeTiming timing,
        decimal prorationAmount,
        DateTimeOffset? effectiveAt)
    {
        UserReference = userReference;
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        NewPlanHandle = newPlanHandle;
        Timing = timing;
        ProrationAmount = prorationAmount;
        EffectiveAt = effectiveAt;
    }

    public string UserReference { get; }

    public BillingSubscription Subscription { get; }

    public string? PreviousPlanHandle { get; }

    public string NewPlanHandle { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>Amount due for the change, in decimal currency units. Zero for a change deferred to renewal.</summary>
    public decimal ProrationAmount { get; }

    public DateTimeOffset? EffectiveAt { get; }
}
