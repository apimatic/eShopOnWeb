using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Models.Subscription;

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListUserSubscriptionsResponse()
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
