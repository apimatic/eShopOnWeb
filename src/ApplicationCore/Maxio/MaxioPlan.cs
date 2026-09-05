namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "Product") within the configured product family.
/// </summary>
public class MaxioPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public int PriceInCents { get; init; }
    public int IntervalCount { get; init; }
    public required string IntervalUnit { get; init; }
}
