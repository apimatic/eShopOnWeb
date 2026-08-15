using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        ProductId = plan.ProductId,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        PriceFormatted = plan.PriceFormatted,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        PriceFormatted = subscription.PriceFormatted,
        NextBillingAt = subscription.NextBillingAt,
    };
}
