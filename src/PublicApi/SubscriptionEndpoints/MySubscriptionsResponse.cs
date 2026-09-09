using System;
using System.Collections.Generic;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing the caller's subscriptions.
/// </summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
