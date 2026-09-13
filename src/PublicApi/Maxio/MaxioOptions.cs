namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioOptions
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string Environment { get; set; } = "US";
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
}
