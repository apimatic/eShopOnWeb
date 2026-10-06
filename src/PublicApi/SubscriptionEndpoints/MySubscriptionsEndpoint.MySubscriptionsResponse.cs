using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing the caller's subscriptions.
/// </summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse() : base()
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public List<SubscriptionDto> Subscriptions { get; set; }
}