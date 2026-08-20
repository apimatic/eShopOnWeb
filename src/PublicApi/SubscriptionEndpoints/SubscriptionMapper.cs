using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };
}
