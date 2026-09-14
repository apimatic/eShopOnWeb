namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
}
