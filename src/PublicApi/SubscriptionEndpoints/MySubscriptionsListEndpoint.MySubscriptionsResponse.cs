using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new List<CustomerSubscriptionDto>();
}
