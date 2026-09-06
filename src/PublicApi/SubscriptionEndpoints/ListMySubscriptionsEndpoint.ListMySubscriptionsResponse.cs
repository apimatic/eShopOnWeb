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

    /// <summary>The shopper's billing customer, or null when they have never subscribed.</summary>
    public BillingCustomerDto? Customer { get; set; }

    /// <summary>Every subscription on the shopper's billing customer, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>The subset of <see cref="Subscriptions"/> that has not reached a terminal state.</summary>
    public int ActiveCount { get; set; }
}
