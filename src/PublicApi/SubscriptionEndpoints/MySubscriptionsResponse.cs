using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the authenticated user's Maxio subscriptions.
/// </summary>
public class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionSummaryDto> Subscriptions { get; set; } = new List<SubscriptionSummaryDto>();
}
