namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A product (subscription plan) as represented in Maxio Advanced Billing.
/// </summary>
public class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
}
