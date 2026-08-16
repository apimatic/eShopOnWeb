using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps application subscription models to the PublicApi DTOs.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static CustomerSubscriptionDto ToDto(this SubscriptionDetails details) => new()
    {
        Id = details.Id,
        State = details.State,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        PriceInCents = details.PriceInCents,
        FormattedPrice = details.FormattedPrice,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
        NextBillingAt = details.NextBillingAt,
        PaymentCollectionMethod = details.PaymentCollectionMethod
    };
}
