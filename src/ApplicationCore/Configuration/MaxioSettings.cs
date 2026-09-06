namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
