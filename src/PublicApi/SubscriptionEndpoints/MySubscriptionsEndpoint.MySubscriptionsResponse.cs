using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
