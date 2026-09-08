using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public MySubscriptionsResponse()
    {
    }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
