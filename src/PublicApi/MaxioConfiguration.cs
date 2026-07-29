namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;
        return $"https://{Subdomain}.chargify.com";
    }
}
