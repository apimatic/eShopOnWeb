namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return $"https://{Subdomain}.maxio.com";
    }
}
