namespace Microsoft.eShopWeb.ApplicationCore.Settings;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (Environment == "production")
        {
            return $"https://{Subdomain}.chargify.com";
        }

        return $"https://{Subdomain}.ebilling.maxio.com";
    }
}
