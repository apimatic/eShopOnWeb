namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class ProductFamilyEnvelope
{
    public ProductFamilyWire? ProductFamily { get; set; }
}

internal class ProductFamilyWire
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}
