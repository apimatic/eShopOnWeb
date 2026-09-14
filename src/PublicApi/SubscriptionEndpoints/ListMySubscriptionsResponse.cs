using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
