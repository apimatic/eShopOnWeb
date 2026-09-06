using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Projects the billing domain models onto the API contract.</summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        IntervalCount = plan.IntervalCount,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        Currency = subscription.Currency,
        State = subscription.State,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        IsLive = subscription.IsLive,
        NextBillingDate = subscription.NextBillingDate,
        CreatedAt = subscription.CreatedAt
    };
}
