using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
