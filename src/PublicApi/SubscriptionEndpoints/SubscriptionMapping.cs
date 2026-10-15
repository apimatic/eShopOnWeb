using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToAmount(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        Price = ToAmount(subscription.PriceInCents),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static decimal? ToAmount(long? cents) => cents is null ? null : cents.Value / 100m;
}
