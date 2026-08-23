using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Name = plan.Name,
        Handle = plan.Handle,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PricePointHandle = plan.PricePointHandle
    };

    public static SubscriptionDto ToDto(this UserSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        ProductName = subscription.ProductName,
        ProductHandle = subscription.ProductHandle,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
