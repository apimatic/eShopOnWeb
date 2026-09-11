namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class ProductResponse
{
    public Product Product { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public ProductFamily ProductFamily { get; set; } = new();
}

public class ProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
