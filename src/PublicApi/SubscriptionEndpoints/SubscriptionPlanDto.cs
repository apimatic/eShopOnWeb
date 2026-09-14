namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring subscription plan available for sign-up, surfaced by GET /api/subscription-plans.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Billing-provider numeric id. Not stable across provider re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring price in the site's currency (major units).</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;
}
