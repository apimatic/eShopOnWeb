using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}
