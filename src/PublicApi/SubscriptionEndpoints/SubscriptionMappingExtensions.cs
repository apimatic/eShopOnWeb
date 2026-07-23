using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain types onto the API's contract types.
/// </summary>
public static class SubscriptionMappingExtensions
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresPaymentMethod = plan.RequiresPaymentMethod
        };
    }

    public static SubscriptionDto ToDto(this CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State.ToString(),
            CustomerId = subscription.CustomerId,
            CustomerReference = subscription.CustomerReference,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PlanPrice = subscription.PlanPrice,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            DelayedCancelAt = subscription.DelayedCancelAt,
            NextPlanHandle = subscription.NextPlanHandle
        };
    }
}
