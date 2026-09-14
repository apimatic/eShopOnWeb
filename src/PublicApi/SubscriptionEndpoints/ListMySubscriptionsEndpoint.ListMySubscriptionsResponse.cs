using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Result of <c>GET /api/my-subscriptions</c>.
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    /// <summary>Every subscription the shopper holds or has held, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>How many of those subscriptions are currently entitling the shopper.</summary>
    public int ActiveCount => Subscriptions.Count(subscription => subscription.IsActive);
}
