namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
