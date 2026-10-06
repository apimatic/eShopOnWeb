using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the subscriptions of the authenticated user.
/// </summary>
public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsListResponse()
    {
    }

    /// <summary>
    /// Subscriptions recorded in Maxio for the caller; empty if never subscribed.
    /// </summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
