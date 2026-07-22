namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a customer can subscribe to. <see cref="Price"/> is in whole currency units.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
    public string BillingPeriod { get; set; }
    public string Description { get; set; }
}
