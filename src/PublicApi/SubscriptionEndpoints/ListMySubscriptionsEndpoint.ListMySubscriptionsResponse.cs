using System;
using System.Collections.Generic;
using System.Linq;
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

    public static ListMySubscriptionsResponse From(IEnumerable<ShopperSubscription> subscriptions, Guid correlationId)
    {
        var response = new ListMySubscriptionsResponse(correlationId);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappings.ToDto));
        return response;
    }
}
