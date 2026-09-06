using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt
    };
}
