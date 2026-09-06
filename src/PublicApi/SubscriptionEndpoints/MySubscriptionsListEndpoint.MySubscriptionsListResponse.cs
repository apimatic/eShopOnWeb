using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse() : base(Guid.NewGuid())
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
