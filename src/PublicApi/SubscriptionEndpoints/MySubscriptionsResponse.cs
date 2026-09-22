using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
