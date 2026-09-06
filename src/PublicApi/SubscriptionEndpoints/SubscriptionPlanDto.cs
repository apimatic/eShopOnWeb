namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to. <see cref="Handle"/> is the identifier callers post back to
/// <c>POST /api/subscriptions</c>; the billing system's numeric ids are deliberately not exposed because
/// they are not stable across catalog re-seeds.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}
