using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
