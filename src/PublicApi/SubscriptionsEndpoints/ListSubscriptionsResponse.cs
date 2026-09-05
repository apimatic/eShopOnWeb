using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class ListSubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new List<SubscriptionDto>();

    public ListSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }
}
