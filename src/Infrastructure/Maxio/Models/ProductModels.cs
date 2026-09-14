namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models mirroring components/schemas/Product.yaml and its { "product": ... }
// envelope (Product-Response.yaml).

/// <summary>Envelope for a single product, per Product-Response.yaml.</summary>
public class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Subset of Product.yaml used to present a subscription plan.</summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
