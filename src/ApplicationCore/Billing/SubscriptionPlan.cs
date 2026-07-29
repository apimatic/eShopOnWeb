namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan a shopper can subscribe to (a product in the billing system's catalog).
/// Handles are stable across catalog re-seeds; numeric ids are not, so callers should
/// prefer <see cref="Handle"/> when subscribing.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit)
{
    /// <summary>The recurring price expressed in major currency units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>A human-friendly rendering of the recurring price, e.g. "299.00 / month".</summary>
    public string FormattedPrice => $"{Price:0.00} / {IntervalUnit}";
}
