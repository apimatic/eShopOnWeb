using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public ListMySubscriptionsResponse()
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public List<SubscriptionDto> Subscriptions { get; set; }
}
