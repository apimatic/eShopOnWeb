using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(SubscriptionDetails s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        CustomerId = s.CustomerId,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt,
        PaymentCollectionMethod = s.PaymentCollectionMethod
    };
}
