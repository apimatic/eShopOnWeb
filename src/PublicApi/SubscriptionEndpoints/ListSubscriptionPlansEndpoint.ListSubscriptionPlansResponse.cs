using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Result of <c>GET /api/subscription-plans</c>.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    /// <summary>Plans available for subscription, cheapest first.</summary>
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
