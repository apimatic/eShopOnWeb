using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();

    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }
}
