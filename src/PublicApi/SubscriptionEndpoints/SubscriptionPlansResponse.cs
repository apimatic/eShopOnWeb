using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansResponse
{
    public List<PlanItem> Plans { get; set; } = new();
}

public class PlanItem
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Price { get; set; }
    public string Interval { get; set; } = "";
}
