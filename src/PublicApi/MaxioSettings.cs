namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = "";

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return $"https://{Subdomain}.maxio.com/api/v1";
    }
}
