using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperSubscriptionMapping
{
    public static ShopperSubscriptionDto ToDto(ShopperSubscription subscription)
    {
        return new ShopperSubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            PriceInCents = subscription.PriceInCents,
            NextBillingDate = subscription.NextBillingDate,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
    }
}
