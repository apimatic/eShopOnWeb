using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.MaxioBillingEndpoints;

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
