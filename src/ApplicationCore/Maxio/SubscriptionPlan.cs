namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "Product") within the store's configured product family.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int IntervalCount { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
