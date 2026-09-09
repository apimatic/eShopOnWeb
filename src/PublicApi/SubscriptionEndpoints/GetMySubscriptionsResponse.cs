using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionStatusDto> Subscriptions { get; set; } = new List<SubscriptionStatusDto>();
}
