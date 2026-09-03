namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio "product") within the configured product family.
/// </summary>
public record SubscriptionPlan
{
    public int? Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public long? PriceInCents { get; init; }
    public string? FormattedPrice { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? Description { get; init; }
}
