namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl;
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
