namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = default!;
}
