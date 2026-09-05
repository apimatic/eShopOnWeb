namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string? BaseUrl { get; set; }
}
