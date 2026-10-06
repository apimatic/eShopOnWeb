using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse()
    {
    }

    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
