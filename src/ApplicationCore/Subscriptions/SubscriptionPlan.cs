namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio "Product"), read live from Advanced Billing.
/// </summary>
public class SubscriptionPlan
{
    public long MaxioProductId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int IntervalCount { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
