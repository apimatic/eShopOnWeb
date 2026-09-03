namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan returned to API clients.
/// </summary>
public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long? PriceInCents { get; set; }
    public string? FormattedPrice { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Description { get; set; }
}
