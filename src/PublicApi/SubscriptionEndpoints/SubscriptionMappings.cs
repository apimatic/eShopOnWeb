using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps the ApplicationCore subscription domain models to the PublicApi DTOs.</summary>
internal static class SubscriptionMappings
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
        IntervalUnit = plan.IntervalUnit,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.NextBillingDate,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        Currency = subscription.Currency,
    };
}
