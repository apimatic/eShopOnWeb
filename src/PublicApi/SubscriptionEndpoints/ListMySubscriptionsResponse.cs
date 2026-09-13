using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse
{
    public List<Microsoft.eShopWeb.ApplicationCore.Interfaces.SubscriptionDto> Subscriptions { get; set; } = new();
}
