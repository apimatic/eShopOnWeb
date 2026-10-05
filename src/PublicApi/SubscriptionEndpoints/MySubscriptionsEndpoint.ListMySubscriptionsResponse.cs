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

    /// <summary>Subscriptions as recorded by Maxio, the billing system of record.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();

    /// <summary>Subscriptions requested but not yet confirmed by Maxio.</summary>
    public List<PendingSubscriptionDto> Pending { get; set; } = new List<PendingSubscriptionDto>();
}
