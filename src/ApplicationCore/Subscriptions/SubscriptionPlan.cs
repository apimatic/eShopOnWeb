namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (Maxio "Product") within the configured product family.
/// </summary>
public class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
}
