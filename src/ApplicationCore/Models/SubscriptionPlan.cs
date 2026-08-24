namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan (a Maxio product within the configured product family).
/// </summary>
public sealed record SubscriptionPlan
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
