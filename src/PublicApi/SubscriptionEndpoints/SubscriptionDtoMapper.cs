using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static SubscriptionDto ToDto(SubscriptionStatus subscription)
        => new()
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            NextBillingDate = subscription.NextBillingDate
        };
}
