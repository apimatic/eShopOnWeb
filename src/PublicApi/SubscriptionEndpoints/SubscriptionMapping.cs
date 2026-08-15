using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps provider-neutral billing models onto the API's wire DTOs.</summary>
internal static class SubscriptionMapping
{
    private const decimal CentsPerUnit = 100m;

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / CentsPerUnit,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PaymentMethodRequired = plan.PaymentMethodRequired
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / CentsPerUnit,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.NextBillingDate,
        CustomerReference = subscription.CustomerReference
    };
}
