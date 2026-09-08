using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionListResponse()
    {
    }

    public List<SubscriptionDetailsDto> Subscriptions { get; set; } = new List<SubscriptionDetailsDto>();
}
