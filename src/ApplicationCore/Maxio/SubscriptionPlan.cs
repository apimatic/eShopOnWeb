namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public long PriceInCents { get; init; }
    public string? Currency { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
}
