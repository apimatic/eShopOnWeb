using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain models onto the API's DTOs. Kept in one place so every
/// subscription endpoint returns the same shape.
/// </summary>
internal static class SubscriptionMapper
{
    public static PlanDto ToDto(this BillingPlan plan) => new PlanDto
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this BillingSubscription subscription) => new SubscriptionDto
    {
        Id = subscription.Id,
        Status = subscription.Status.ToString(),
        ProviderState = subscription.ProviderState,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PlanPrice = subscription.PlanPrice,
        Balance = subscription.Balance,
        CustomerReference = subscription.CustomerReference,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        NextPlanHandle = subscription.NextPlanHandle
    };

    public static List<SubscriptionDto> ToDtos(this IReadOnlyCollection<BillingSubscription> subscriptions) =>
        subscriptions.Select(ToDto).ToList();

    public static List<PlanDto> ToDtos(this IReadOnlyCollection<BillingPlan> plans) =>
        plans.Select(ToDto).ToList();
}
