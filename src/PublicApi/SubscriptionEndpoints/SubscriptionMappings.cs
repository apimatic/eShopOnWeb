using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) =>
        new()
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        };

    public static SubscriptionDto ToDto(CustomerSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            PriceInCents = subscription.PriceInCents,
            NextBillingDate = subscription.NextBillingDate
        };

    public static List<SubscriptionDto> ToDtos(IEnumerable<CustomerSubscription> subscriptions)
    {
        var list = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions)
        {
            list.Add(ToDto(subscription));
        }

        return list;
    }
}
