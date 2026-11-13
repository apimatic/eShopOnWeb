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

    /// <summary>The billing system's customer id for the caller; null if they have never subscribed.</summary>
    public int? BillingCustomerId { get; set; }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>Subscribe requests whose outcome is still being confirmed with the billing system.</summary>
    public List<PendingSubscriptionDto> Pending { get; set; } = new();
}
