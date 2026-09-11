using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}
