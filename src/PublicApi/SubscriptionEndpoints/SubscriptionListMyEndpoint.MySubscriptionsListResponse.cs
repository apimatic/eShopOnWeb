using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the authenticated user's subscriptions.
/// </summary>
public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
