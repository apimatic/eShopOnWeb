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

    /// <summary>Live subscriptions first, then the ended ones, newest first within each group.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>True when the shopper holds at least one subscription that is still current.</summary>
    public bool HasActiveSubscription { get; set; }
}
