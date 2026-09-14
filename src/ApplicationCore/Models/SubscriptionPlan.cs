namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscribable plan sourced live from the billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = default!;
}
