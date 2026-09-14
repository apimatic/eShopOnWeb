using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
