using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    /// <summary>Every subscription belonging to the caller, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>True when at least one subscription currently grants access to a paid plan.</summary>
    public bool HasActiveSubscription { get; set; }
}
