namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class ProductResponse
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string handle { get; set; } = string.Empty;
    public long price_in_cents { get; set; }
    public int interval { get; set; }
    public string interval_unit { get; set; } = string.Empty;
}
