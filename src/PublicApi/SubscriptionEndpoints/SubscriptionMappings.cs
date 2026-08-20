using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        NextBillingAt = subscription.NextBillingAt
    };

    public static List<SubscriptionPlanDto> ToPlanDtos(this IEnumerable<SubscriptionPlan> plans) =>
        plans.Select(ToDto).ToList();

    public static List<SubscriptionDto> ToSubscriptionDtos(this IEnumerable<CustomerSubscription> subscriptions) =>
        subscriptions.Select(ToDto).ToList();
}
