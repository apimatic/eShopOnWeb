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

    /// <summary>Every subscription held by the shopper, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>True when at least one subscription currently entitles the shopper to a plan.</summary>
    public bool HasLiveSubscription { get; set; }
}
