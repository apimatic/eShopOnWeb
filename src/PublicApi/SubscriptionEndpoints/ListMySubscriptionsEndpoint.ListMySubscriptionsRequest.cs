using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Resolved from the JWT identity by the route (never bound from the request).
    /// </summary>
    internal Microsoft.eShopWeb.ApplicationCore.Integration.Maxio.SubscriberProfile? Subscriber { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
