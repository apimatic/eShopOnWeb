using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsResponse : BaseResponse
{
    public GetUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetUserSubscriptionsResponse()
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
