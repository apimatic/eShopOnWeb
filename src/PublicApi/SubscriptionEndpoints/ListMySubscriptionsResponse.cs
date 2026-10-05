using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The authenticated shopper's subscriptions in Maxio Advanced Billing.
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse()
    {
    }

    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}