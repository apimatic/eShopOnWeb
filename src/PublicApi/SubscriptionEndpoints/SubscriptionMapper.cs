using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Projects the domain types onto the API's DTOs.</summary>
internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        UserReference = subscription.UserReference,
        CustomerId = subscription.CustomerId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt
    };

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };
}
