namespace Microsoft.eShopWeb.Infrastructure.Maxio.Json;

internal sealed class ProductEnvelope
{
    public ProductJson? Product { get; set; }
}

internal sealed class ProductJson
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public ProductFamilyJson? ProductFamily { get; set; }
}

internal sealed class ProductFamilyJson
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}
