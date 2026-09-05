namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string Subdomain { get; set; } = "cp-exp-1";
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string? BaseUrl { get; set; }
}
