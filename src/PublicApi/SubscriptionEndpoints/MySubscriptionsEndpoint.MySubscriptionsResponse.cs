using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse() { }
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDetailsDto> Subscriptions { get; set; } = new();
}
