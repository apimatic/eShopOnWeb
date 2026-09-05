using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        IntervalCount = plan.IntervalCount,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };
}
