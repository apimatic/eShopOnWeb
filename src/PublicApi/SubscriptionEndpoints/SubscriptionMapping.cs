using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(MaxioPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresCreditCard = plan.RequiresCreditCard
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / 100m,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
