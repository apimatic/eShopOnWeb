namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscribable plan, as read from the Maxio product catalog.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal PriceAmount { get; init; }
    public int BillingIntervalCount { get; init; }
    public string BillingIntervalUnit { get; init; } = string.Empty;
}
