using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the authenticated user's subscriptions.
/// </summary>
public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse() { }

    public MySubscriptionListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
