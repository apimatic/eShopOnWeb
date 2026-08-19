namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A sellable Maxio product in the configured product family.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
