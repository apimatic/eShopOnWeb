using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionDto ToDto(this ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        ProductPriceInCents = subscription.ProductPriceInCents,
        ProductPrice = subscription.ProductPriceInCents / 100m,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference
    };
}
