using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsListResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
