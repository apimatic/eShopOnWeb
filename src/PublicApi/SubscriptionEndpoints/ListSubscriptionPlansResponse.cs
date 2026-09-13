using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
