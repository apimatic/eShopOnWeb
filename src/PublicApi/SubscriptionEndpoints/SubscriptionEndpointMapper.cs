using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) =>
        new(
            plan.Handle,
            plan.Name,
            plan.Description,
            plan.PriceInCents,
            plan.Interval,
            plan.IntervalUnit,
            plan.RequiresPaymentMethod);

    public static SubscriptionDto ToDto(this SubscriptionSummary subscription) =>
        new(
            subscription.Reference,
            subscription.ProductHandle,
            subscription.PlanName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingDate);
}
