using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.MySubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
