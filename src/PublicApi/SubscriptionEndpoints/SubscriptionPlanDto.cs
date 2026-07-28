namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API representation of a subscribable plan.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle; pass this to <c>POST /api/subscriptions</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }
}
