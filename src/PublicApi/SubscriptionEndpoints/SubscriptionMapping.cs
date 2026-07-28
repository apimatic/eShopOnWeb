using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects domain billing models to their API DTOs.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        ProductId = plan.ProductId,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        CustomerId = summary.CustomerId,
        State = summary.State,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        PriceInCents = summary.PriceInCents,
        FormattedPrice = summary.FormattedPrice,
        NextBillingDate = summary.NextBillingDate,
        CurrentPeriodStartedAt = summary.CurrentPeriodStartedAt,
        CreatedAt = summary.CreatedAt
    };
}
