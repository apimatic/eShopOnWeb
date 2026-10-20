using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToAmount(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresCreditCard = plan.RequiresCreditCard
    };

    public static SubscriptionDto ToDto(this SubscriptionSummary subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = ToAmount(subscription.PriceInCents),
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference
    };

    private static decimal? ToAmount(long? cents) => cents is null ? null : cents.Value / 100m;
}
