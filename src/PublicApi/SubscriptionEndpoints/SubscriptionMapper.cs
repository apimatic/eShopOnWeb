using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(MaxioPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.PriceInCents / 100m,
        Currency = plan.Currency,
        IntervalCount = plan.IntervalCount,
        IntervalUnit = plan.IntervalUnit,
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / 100m,
        Currency = subscription.Currency,
        NextBillingDate = subscription.NextBillingDate,
    };
}
