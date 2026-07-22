using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain types onto the API's response shapes.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.BillingSubscriptionId,
        BuyerId = subscription.BuyerId,
        BillingCustomerId = subscription.BillingCustomerId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
    };
}
