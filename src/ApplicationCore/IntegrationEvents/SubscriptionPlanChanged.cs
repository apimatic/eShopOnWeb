using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a subscription has moved to a different plan (UC3), carrying old plan to new
/// plan and the proration that was applied. Delivery is in-process and best-effort.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userName, Subscription subscription, SubscriptionPlan previousPlan, PlanChangeTiming timing, int paymentDueInCents)
    {
        UserName = userName;
        Subscription = subscription;
        PreviousPlan = previousPlan;
        Timing = timing;
        PaymentDueInCents = paymentDueInCents;
    }

    public string UserName { get; }
    public Subscription Subscription { get; }
    public SubscriptionPlan PreviousPlan { get; }
    public SubscriptionPlan NewPlan => Subscription.Plan;
    public PlanChangeTiming Timing { get; }
    public int PaymentDueInCents { get; }
}
