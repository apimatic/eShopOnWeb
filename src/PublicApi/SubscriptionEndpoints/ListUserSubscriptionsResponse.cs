using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse()
    {
    }

    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; } = new();
}
