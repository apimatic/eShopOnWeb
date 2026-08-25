using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
