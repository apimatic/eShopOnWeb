using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToPlanDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static ShopperSubscriptionDto ToSubscriptionDto(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit
    };
}
