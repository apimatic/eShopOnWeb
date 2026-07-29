using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps provider-agnostic subscription domain models to the API DTOs.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        Currency = plan.Currency,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CustomerId = subscription.CustomerId,
        CreatedAt = subscription.CreatedAt,
    };
}
