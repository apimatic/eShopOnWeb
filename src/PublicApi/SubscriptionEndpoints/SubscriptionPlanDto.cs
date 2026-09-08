namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan on the configured Maxio product family.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? PriceInCents { get; set; }
    public bool RequiresCreditCard { get; set; }
}
