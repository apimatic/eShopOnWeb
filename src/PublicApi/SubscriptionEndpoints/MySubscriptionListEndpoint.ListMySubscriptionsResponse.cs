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

    /// <summary>False when the user has no billing account yet (never subscribed).</summary>
    public bool HasBillingAccount { get; set; }

    /// <summary>Set while a subscribe attempt's outcome is still being confirmed.</summary>
    public string? PendingSubscriptionReference { get; set; }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
