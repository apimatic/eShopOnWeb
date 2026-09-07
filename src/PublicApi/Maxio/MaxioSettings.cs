namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var host = Environment switch
        {
            "production" => "chargify.com",
            "eu" => "ebilling.maxio.com",
            _ => "chargify.com"
        };

        return $"https://{Subdomain}.{host}";
    }
}
