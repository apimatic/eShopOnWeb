using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
