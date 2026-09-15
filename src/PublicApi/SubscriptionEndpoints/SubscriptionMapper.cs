using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        CreatedAt = subscription.CreatedAt
    };
}
