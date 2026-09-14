using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Currency = plan.Currency,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            PaymentMethodRequired = plan.PaymentMethodRequired
        };
    }

    public static CustomerSubscriptionDto ToDto(CustomerSubscription subscription)
    {
        return new CustomerSubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CreatedAt = subscription.CreatedAt,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }
}
