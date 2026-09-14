namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan (Maxio "product") available for subscription, resolved from the configured
/// product family - never hardcoded to a specific catalog.
/// </summary>
public class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool Taxable { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}
