using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMySubscriptionsResponse()
    {
    }

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
