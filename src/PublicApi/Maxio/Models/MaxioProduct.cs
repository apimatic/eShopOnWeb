namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A product (plan) in Maxio Advanced Billing.
/// </summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public int DefaultProductPricePointId { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}
