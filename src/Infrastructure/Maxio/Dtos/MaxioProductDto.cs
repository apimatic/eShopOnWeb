namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

/// <summary>Product resource as returned by the Maxio API.</summary>
public class MaxioProductDto
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
    public MaxioProductFamilyDto? ProductFamily { get; set; }
}
