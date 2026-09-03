using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
