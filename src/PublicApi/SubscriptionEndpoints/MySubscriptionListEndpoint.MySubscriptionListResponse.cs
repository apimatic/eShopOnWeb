using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionListResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
