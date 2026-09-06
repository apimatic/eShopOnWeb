using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    /// <summary>Every subscription the caller holds, most recently created first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>Count of those subscriptions that still entitle the caller to their plan.</summary>
    public int LiveCount { get; set; }
}
