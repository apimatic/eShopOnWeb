using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps ApplicationCore billing models onto the PublicApi response DTOs.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        ProductFamilyName = plan.ProductFamilyName
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextBillingAt
    };
}
