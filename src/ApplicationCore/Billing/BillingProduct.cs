namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Product as returned by the billing system of record (Maxio).
/// </summary>
public sealed class BillingProduct
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public string? ProductFamilyHandle { get; init; }
}
