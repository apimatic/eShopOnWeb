namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class ProductResponse
{
    public MaxioProduct? Product { get; set; }
}
