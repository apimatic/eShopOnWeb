namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string Subdomain { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string BaseUrl { get; set; } = string.Empty;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;
        return $"https://{Subdomain}.chargify.com";
    }
}
