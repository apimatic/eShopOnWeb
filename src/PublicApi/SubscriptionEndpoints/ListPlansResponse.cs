using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}
