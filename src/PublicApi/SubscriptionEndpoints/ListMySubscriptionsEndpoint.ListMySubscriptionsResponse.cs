using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionResult> Subscriptions { get; set; } = new List<SubscriptionResult>();
}
