using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public IReadOnlyList<SubscriptionDto>? Subscriptions { get; set; }
    public string? ErrorMessage { get; set; }
}
