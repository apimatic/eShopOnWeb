using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class ListSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
