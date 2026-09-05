namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio Advanced Billing product (i.e. a subscribable plan) within a product family.
/// </summary>
public class MaxioProduct
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int IntervalCount { get; init; }
    public string IntervalUnit { get; init; } = "month";
    public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    public bool HasTrial { get; init; }
}
