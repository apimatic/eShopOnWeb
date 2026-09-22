using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>True when the plan list was cut short by the paging safety cap (partial result).</summary>
    public bool Truncated { get; set; }
}
