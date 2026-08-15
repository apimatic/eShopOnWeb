using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps provider-agnostic billing models onto API DTOs.</summary>
internal static class SubscriptionDtoMapper
{
    private const decimal CentsPerUnit = 100m;

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        ProductId = plan.ProductId,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / CentsPerUnit,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        ProductId = subscription.ProductId,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / CentsPerUnit : null,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
    };
}
