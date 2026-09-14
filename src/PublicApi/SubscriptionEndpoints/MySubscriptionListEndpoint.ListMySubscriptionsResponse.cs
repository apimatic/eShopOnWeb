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

    /// <summary>The caller's subscriptions, newest first. Empty when they have never subscribed.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
