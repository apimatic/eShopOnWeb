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

    /// <summary>The caller's billing customer reference, stable across restarts of this application.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Every subscription the caller owns, most recently created first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
