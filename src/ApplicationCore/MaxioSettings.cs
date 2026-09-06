namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

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

        // Default Maxio sandbox URL pattern
        return $"https://{Subdomain}.chargify.com";
    }
}
