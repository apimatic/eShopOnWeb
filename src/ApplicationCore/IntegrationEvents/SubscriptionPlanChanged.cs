using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a subscription has moved to a different plan (UC3 step 5).
/// </summary>
/// <remarks>Delivery is in-process and best-effort (plan.md §2.5).</remarks>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(Subscription subscription,
        BillingPlan previousPlan,
        BillingPlan newPlan,
        PlanChangeTiming timing,
        decimal netAmount)
    {
        Subscription = subscription;
        PreviousPlan = previousPlan;
        NewPlan = newPlan;
        Timing = timing;
        NetAmount = netAmount;
    }

    public Subscription Subscription { get; }

    public BillingPlan PreviousPlan { get; }

    public BillingPlan NewPlan { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The previewed net amount in dollars: positive is a charge, negative a credit.</summary>
    public decimal NetAmount { get; }
}
