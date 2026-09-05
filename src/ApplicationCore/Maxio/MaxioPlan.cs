namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A sellable plan (Maxio product + its default price point) within a product family.
/// </summary>
public record MaxioPlan(
    string Handle,
    string Name,
    int PriceInCents,
    string Currency,
    int IntervalCount,
    string IntervalUnit,
    string ProductFamilyHandle);
