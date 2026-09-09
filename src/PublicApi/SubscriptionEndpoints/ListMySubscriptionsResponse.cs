using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public ListMySubscriptionsResponse(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();
}
