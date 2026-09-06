namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
}
