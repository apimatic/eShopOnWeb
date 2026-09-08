using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListResponse : BaseResponse
{
    public SubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionListResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
