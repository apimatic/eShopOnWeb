namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (Maxio product) surfaced by the API.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable Maxio API handle, used as the plan identifier in requests.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the site's currency (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>ISO-4217 currency code (e.g. USD).</summary>
    public string? Currency { get; set; }

    public int Interval { get; set; } = 1;

    /// <summary>Billing interval unit: month or day.</summary>
    public string? IntervalUnit { get; set; }
}
