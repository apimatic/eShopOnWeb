namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as exposed over the API. <see cref="Price"/> is in whole currency units.
/// </summary>
public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
}
