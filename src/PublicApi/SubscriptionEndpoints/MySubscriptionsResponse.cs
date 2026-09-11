using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse
{
    public List<SubscribeResponse> Subscriptions { get; set; } = new();
}
