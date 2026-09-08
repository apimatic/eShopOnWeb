using System.Collections.Generic;
using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for GET /api/my-subscriptions.</summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse()
    {
    }

    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
