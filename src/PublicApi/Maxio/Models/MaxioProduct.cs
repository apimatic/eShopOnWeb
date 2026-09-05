namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Mirrors the relevant fields of the Maxio "Product" schema (maxio-spec/components/schemas/Product.yaml).
/// A Maxio "Product" is what eShopOnWeb exposes to shoppers as a subscribable "Plan".
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public class MaxioProductFamilyRef
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}
