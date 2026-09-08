using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionsResponse : BaseResponse
{
    public ListSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
