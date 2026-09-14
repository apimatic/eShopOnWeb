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

    /// <summary>The signed-in shopper's user name, echoed back for confirmation.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Every subscription held by the shopper, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>Only the subscriptions that are still live.</summary>
    public List<SubscriptionDto> ActiveSubscriptions { get; set; } = new();
}
