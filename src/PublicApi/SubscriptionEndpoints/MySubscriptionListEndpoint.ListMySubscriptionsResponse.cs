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

    /// <summary>The eShopOnWeb account these subscriptions belong to.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Every subscription held by the shopper, newest first, including ended ones.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>Count of subscriptions that still entitle the shopper to the product.</summary>
    public int LiveCount { get; set; }
}
