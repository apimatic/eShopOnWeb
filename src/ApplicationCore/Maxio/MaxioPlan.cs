namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribeable plan (a Maxio "product"), as returned by
/// GET /product_families/handle:{handle}/products.json.
/// </summary>
public class MaxioPlan
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
