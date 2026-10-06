namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as returned by the API.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;
}