using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static SubscriptionDto From(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
