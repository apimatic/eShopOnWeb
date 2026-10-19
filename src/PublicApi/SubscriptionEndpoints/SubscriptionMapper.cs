using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToPlanDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Price = ToPrice(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(ShopperSubscription subscription) => new()
    {
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = ToPrice(subscription.PriceInCents),
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt,
        NextAssessmentAt = subscription.NextAssessmentAt
    };

    private static decimal? ToPrice(long? priceInCents) =>
        priceInCents.HasValue ? priceInCents.Value / 100m : null;
}
