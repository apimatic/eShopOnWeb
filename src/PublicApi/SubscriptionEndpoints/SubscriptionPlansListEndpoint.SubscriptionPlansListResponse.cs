using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
