namespace Microsoft.eShopWeb.Infrastructure;

public class MaxioConfiguration
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return $"https://{Subdomain}.maxio.com/api/v1";
    }
}
