namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string? BaseUrl { get; set; }
    public string Environment { get; set; } = "US";
}
