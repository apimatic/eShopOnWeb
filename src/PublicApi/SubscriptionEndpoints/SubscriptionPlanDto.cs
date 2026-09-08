namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (a Maxio product) shown in the catalog. <see cref="Handle"/> is
/// the stable identifier callers pass to <c>POST /api/subscriptions</c>.
/// </summary>
public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Recurring price in cents (minor units of the site currency).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price expressed in major units (e.g. dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing frequency, e.g. 1 for monthly.</summary>
    public int Interval { get; set; }

    /// <summary>Billing frequency unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }
}
