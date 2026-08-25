namespace Microsoft.eShopWeb.PublicApi.Billing;

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
