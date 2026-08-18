namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
}
