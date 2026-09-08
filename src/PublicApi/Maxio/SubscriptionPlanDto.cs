namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; } = 1;

    public string IntervalUnit { get; set; } = string.Empty;
}
