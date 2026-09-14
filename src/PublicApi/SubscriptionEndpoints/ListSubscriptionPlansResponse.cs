using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
