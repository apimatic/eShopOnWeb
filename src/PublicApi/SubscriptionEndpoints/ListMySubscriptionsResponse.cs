using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<ShopperSubscriptionDto> Subscriptions { get; set; } = new();
}
