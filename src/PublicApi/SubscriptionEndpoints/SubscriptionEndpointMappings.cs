using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        ProductId = plan.MaxioProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this ShopperSubscription subscription) => new()
    {
        Id = subscription.MaxioSubscriptionId,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
