using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
