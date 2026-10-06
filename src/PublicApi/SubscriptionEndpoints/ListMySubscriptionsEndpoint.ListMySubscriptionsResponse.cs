using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse() { }

    public List<SubscriptionSummaryDto> Subscriptions { get; } = new List<SubscriptionSummaryDto>();

    public List<string> Errors { get; } = new List<string>();
}