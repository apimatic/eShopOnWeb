using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    public static SubscriptionDto FromSubscription(CustomerSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
}
