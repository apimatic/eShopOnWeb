namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan offered to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable handle of the plan; use as the planHandle when subscribing.
    /// </summary>
    public string Handle { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price in minor currency units (e.g. cents) — 29900 means 299.00/mo.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// Number of billing units between charges (e.g. 1).
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Billing unit (e.g. "month").
    /// </summary>
    public string IntervalUnit { get; set; } = default!;

    /// <summary>
    /// True when the plan requires a payment method at signup.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }
}
