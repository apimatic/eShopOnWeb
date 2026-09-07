using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListUserSubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
