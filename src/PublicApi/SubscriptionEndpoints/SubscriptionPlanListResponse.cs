using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
