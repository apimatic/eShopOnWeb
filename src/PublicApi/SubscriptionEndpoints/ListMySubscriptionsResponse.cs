using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
