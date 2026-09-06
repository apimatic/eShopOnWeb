using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
