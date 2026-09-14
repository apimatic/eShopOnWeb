namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (Maxio product) exposed for browsing. Price is the plan's default price point.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable Maxio product handle (e.g. "eshop-pro").</summary>
    public string? Handle { get; set; }

    /// <summary>Display name of the plan.</summary>
    public string? Name { get; set; }

    /// <summary>Recurring charge for one interval, in the site's default currency.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO currency code (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>Number of interval units per billing cycle.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit: "month", "day", etc.</summary>
    public string? IntervalUnit { get; set; }
}
