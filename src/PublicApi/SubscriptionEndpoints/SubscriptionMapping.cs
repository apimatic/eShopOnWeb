using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this SubscriptionDetails s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt,
        CustomerId = s.CustomerId,
        CustomerReference = s.CustomerReference,
        PaymentCollectionMethod = s.PaymentCollectionMethod,
        AlreadyExisted = s.AlreadyExisted
    };
}
