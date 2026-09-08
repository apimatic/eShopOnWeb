using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.MySubscriptionsEndpoints;

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsListResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
