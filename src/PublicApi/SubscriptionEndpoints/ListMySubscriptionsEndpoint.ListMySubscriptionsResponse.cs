using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
        Subscriptions = new List<SubscriptionDto>();
    }

    public IList<SubscriptionDto> Subscriptions { get; set; }
}
