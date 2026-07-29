using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Manual mapping from ApplicationCore subscription models to API DTOs.</summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CreatedAt = subscription.CreatedAt
    };
}
