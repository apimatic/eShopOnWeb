namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Identified by its <see cref="Handle"/>: handles are
/// stable across catalog re-seeds, numeric ids are not.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod)
{
    /// <summary>The recurring price as a currency amount (<see cref="PriceInCents"/> / 100).</summary>
    public decimal Price => PriceInCents / 100m;
}
