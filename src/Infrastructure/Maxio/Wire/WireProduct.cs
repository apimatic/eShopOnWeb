namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Mirrors maxio-spec/components/schemas/Product.yaml (only the fields eShopOnWeb consumes).
internal class WireProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
}

internal class ProductEnvelope
{
    public WireProduct? Product { get; set; }
}
