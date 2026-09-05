namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return $"https://{Subdomain}.chargify.com";
    }
}
