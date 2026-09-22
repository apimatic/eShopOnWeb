using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
